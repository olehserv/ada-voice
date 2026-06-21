using System.Collections.Concurrent;
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Passthrough;
using AdaVoice.Audio.Playback;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Audio.Engine;

/// <summary>
/// Owns the audio graph's lifecycle and state. Driven by a single command queue processed on
/// one thread (see <see cref="DrainPending"/> / <see cref="TryProcessNext"/>), so all state
/// transitions and stream open/close happen serialized — no locks in the core logic. Design:
/// docs/superpowers/specs/2026-06-15-audio-engine-design.md. Built up one transition per task;
/// fault/Degraded handling and teardown arrive in later slices.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const int WatchdogIntervalMs = 100;

    // How long the cable render may go without a read before we treat it as a stall (design §2.2).
    private const int StallThresholdMs = 500;

    // Rebuild backoff while Degraded: 250 ms → 500 ms → 1 s → 2 s → 5 s, then steady 5 s polling
    // (design §2.2). A device-arrived event (commit 4c) can shortcut this.
    private static readonly int[] Backoff = [250, 500, 1000, 2000, 5000];

    private readonly IAudioDeviceFactory _factory;
    private readonly IEngineClock _clock;
    private readonly PhrasePlayerOptions? _playerOptions;
    private readonly BlockingCollection<EngineCommand> _queue = new();

    // Live graph (null when Stopped).
    private IAudioCaptureDevice? _capture;
    private IAudioRenderDevice? _cableRender;
    private MicPassthrough? _passthrough;
    private MixingSampleProvider? _mixer;
    private PhrasePlayer? _player;
    private CableGate? _gate;
    private IDisposable? _watchdog;

    // Degraded bookkeeping: the alarm stream, the state to return to after recovery, and which
    // stream broke (so the rebuild in commit 4b touches only that one).
    private IAudioRenderDevice? _alarmRender;
    private EngineState _restoreState;
    private DeviceRole _faultedRole;
    private int _attempt;          // how many rebuild attempts have failed in this Degraded spell
    private long _nextAttemptMs;   // clock time of the next allowed rebuild attempt

    public AudioEngine(IAudioDeviceFactory factory, IEngineClock clock, PhrasePlayerOptions? playerOptions = null)
    {
        _factory = factory;
        _clock = clock;
        _playerOptions = playerOptions;
    }

    /// <summary>The current state. Updated on the control thread; safe to read for display.</summary>
    public EngineState State { get; private set; } = EngineState.Stopped;

    /// <summary>Raised on the control thread for every state change, drift event, and rebuild.</summary>
    public event EventHandler<EngineEvent>? Events;

    // Public API: each call only enqueues a command and returns.
    public void Start() => Post(new EngineCommand.Start());
    public void Stop() => Post(new EngineCommand.Stop());
    public void Play(Phrase phrase) => Post(new EngineCommand.Play(phrase));
    public void StopPhrase() => Post(new EngineCommand.StopPhrase());
    public void EnterOffAir() => Post(new EngineCommand.EnterOffAir());
    public void ExitOffAir() => Post(new EngineCommand.ExitOffAir());

    public void Post(EngineCommand command) => _queue.Add(command);

    /// <summary>Process every queued command now, on the calling thread. Used by tests and the runner.</summary>
    public void DrainPending()
    {
        while (_queue.TryTake(out var command))
            Handle(command);
    }

    /// <summary>Block up to <paramref name="timeoutMs"/> for one command, process it. Used by the runner.</summary>
    public bool TryProcessNext(int timeoutMs)
    {
        if (!_queue.TryTake(out var command, timeoutMs))
            return false;
        Handle(command);
        return true;
    }

    private void Handle(EngineCommand command)
    {
        switch (command)
        {
            case EngineCommand.Start: HandleStart(); break;
            case EngineCommand.Stop: HandleStop(); break;
            case EngineCommand.EnterOffAir: HandleEnterOffAir(); break;
            case EngineCommand.ExitOffAir: HandleExitOffAir(); break;
            case EngineCommand.Play play: HandlePlay(play.Phrase); break;
            case EngineCommand.StopPhrase: HandleStopPhrase(); break;
            case EngineCommand.StreamFaulted f: HandleStreamFaulted(f.Role, f.Error); break;
            case EngineCommand.WatchdogTick: HandleWatchdogTick(); break;
            // more cases added in later tasks
        }
    }

    private void HandleStart()
    {
        if (State != EngineState.Stopped)
            return;

        BuildGraph();
        SetState(EngineState.Live);
    }

    private void HandleStop()
    {
        if (State == EngineState.Stopped)
            return;

        TeardownGraph();
        SetState(EngineState.Stopped);
    }

    private void HandleStreamFaulted(DeviceRole role, Exception? error)
    {
        // Already broken (or stopped): one fault is enough; ignore the rest until we recover.
        // In-spec limit (design §2.2 "ignore the rest until we recover"): if the other stream also
        // dies while Degraded, we do not track it — recovery of the first restores Live even if the
        // second is now dead. Cascading-fault handling is deliberately out of scope for this slice.
        if (State is EngineState.Degraded or EngineState.Stopped)
            return;

        EnterDegraded(role, error?.Message);
    }

    private void HandleWatchdogTick()
    {
        switch (State)
        {
            // While we believe the cable is live, a stale gate stamp means the render thread
            // stopped pulling — treat that as a cable fault.
            case EngineState.Live or EngineState.OffAir:
                if (_clock.NowMs - _gate!.LastReadMs > StallThresholdMs)
                    EnterDegraded(DeviceRole.Cable, "cable render stalled");
                break;

            // While Degraded, the same tick drives the rebuild schedule (one clock, no extra timer).
            case EngineState.Degraded:
                if (_clock.NowMs >= _nextAttemptMs)
                    AttemptRebuild();
                break;
        }
    }

    /// <summary>
    /// Enter Degraded from a fault: remember where to return to and which stream broke, sound the
    /// alarm, and announce the state. The dead device is left alone here — disposing and recreating
    /// it belongs to the rebuild (commit 4b), so we never double-dispose.
    /// </summary>
    private void EnterDegraded(DeviceRole role, string? error)
    {
        _restoreState = State;
        _faultedRole = role;
        _attempt = 0;
        _nextAttemptMs = _clock.NowMs + Backoff[0];
        StartAlarm();
        SetState(EngineState.Degraded, error);
    }

    /// <summary>
    /// One rebuild attempt for the broken stream only (design §2.4). Success silences the alarm and
    /// returns to where we were; a transient failure schedules the next attempt with backoff; a
    /// terminal failure stops the engine loudly.
    /// </summary>
    private void AttemptRebuild()
    {
        try
        {
            if (_faultedRole == DeviceRole.Cable)
                RebuildCable();
            else
                RebuildMic();
        }
        catch (AudioDeviceException ex) when (ex.IsTransient)
        {
            Raise(new EngineEvent.RebuildResult(_faultedRole, Success: false, _attempt));
            _attempt++;
            _nextAttemptMs = _clock.NowMs + Backoff[Math.Min(_attempt, Backoff.Length - 1)];
            return;
        }
        catch (AudioDeviceException ex)
        {
            // Terminal (non-recoverable) error: stop everything and surface it loudly.
            TeardownGraph();
            SetState(EngineState.Stopped, ex.Message);
            return;
        }

        StopAlarm();
        Raise(new EngineEvent.RebuildResult(_faultedRole, Success: true, _attempt));
        _gate!.IsOpen = _restoreState == EngineState.Live; // keep silence if we return to OFF AIR
        SetState(_restoreState);
    }

    private void RebuildCable()
    {
        DisposeCable();
        _cableRender = _factory.CreateRender(DeviceRole.Cable);
        _cableRender.StateChanged += OnCableStateChanged;
        _cableRender.Init(_gate!);
        _cableRender.Start();
    }

    private void RebuildMic()
    {
        DisposeMicChain();
        _capture = _factory.CreateCapture(DeviceRole.Mic);
        _capture.StateChanged += OnCaptureStateChanged;
        _passthrough = new MicPassthrough(_capture);
        _passthrough.Drift += OnDrift;
        _mixer!.AddMixerInput(_passthrough.Output);
        _capture.Start();
    }

    private void StartAlarm()
    {
        try
        {
            _alarmRender = _factory.CreateRender(DeviceRole.Alarm);
            _alarmRender.Init(new AlarmTone(AudioFormats.Engine));
            _alarmRender.Start();
        }
        catch (AudioDeviceException)
        {
            // Honest limit (design §2.4): if even the system default output is gone we cannot
            // make sound. Stay Degraded so the visual banner still shows; the host logs it.
            _alarmRender = null;
        }
    }

    private void StopAlarm()
    {
        if (_alarmRender is null)
            return;

        _alarmRender.Stop();
        _alarmRender.Dispose();
        _alarmRender = null;
    }

    private void HandlePlay(Phrase phrase)
    {
        // Only Live plays. While OffAir the recorder owns the cable, so a phrase is ignored
        // (design spec §2.2). The core does no logging; the host can log the ignore if wanted.
        if (State != EngineState.Live)
            return;

        _player!.Play(phrase);
    }

    private void HandleStopPhrase()
    {
        if (State != EngineState.Live)
            return;

        _player!.Stop();
    }

    private void HandleEnterOffAir()
    {
        if (State != EngineState.Live)
            return;

        _gate!.IsOpen = false; // stream keeps pulling; the gate just emits silence
        SetState(EngineState.OffAir);
    }

    private void HandleExitOffAir()
    {
        if (State != EngineState.OffAir)
            return;

        _gate!.IsOpen = true;
        SetState(EngineState.Live);
    }

    private void BuildGraph()
    {
        _capture = _factory.CreateCapture(DeviceRole.Mic);
        _capture.StateChanged += OnCaptureStateChanged;

        _passthrough = new MicPassthrough(_capture);
        _passthrough.Drift += OnDrift;

        _mixer = new MixingSampleProvider(AudioFormats.Engine) { ReadFully = true };
        _mixer.AddMixerInput(_passthrough.Output);

        _player = new PhrasePlayer(_mixer, _passthrough, _playerOptions);
        _gate = new CableGate(_mixer, _clock);

        _cableRender = _factory.CreateRender(DeviceRole.Cable);
        _cableRender.StateChanged += OnCableStateChanged;
        _cableRender.Init(_gate);
        _cableRender.Start();
        _capture.Start();

        _watchdog = _clock.SchedulePeriodic(WatchdogIntervalMs, () => Post(new EngineCommand.WatchdogTick()));
    }

    private void OnCaptureStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        if (e.State == DeviceState.Faulted)
            Post(new EngineCommand.StreamFaulted(DeviceRole.Mic, e.Error));
    }

    private void OnCableStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        if (e.State == DeviceState.Faulted)
            Post(new EngineCommand.StreamFaulted(DeviceRole.Cable, e.Error));
    }

    private void OnDrift(object? sender, DriftEventArgs e)
        => Raise(new EngineEvent.DriftLogged(e.Kind));

    private void SetState(EngineState state, string? error = null)
    {
        State = state;
        Raise(new EngineEvent.StateChanged(state, error));
    }

    private void Raise(EngineEvent e) => Events?.Invoke(this, e);

    /// <summary>
    /// Stop and dispose the whole live graph and null every part, so the engine is back to a
    /// clean Stopped shape. The one place that guarantees every stream — including the alarm —
    /// is silenced; both <see cref="HandleStop"/> and <see cref="Dispose"/> go through here.
    /// </summary>
    private void TeardownGraph()
    {
        _watchdog?.Dispose();
        _watchdog = null;

        StopAlarm(); // never leave the alarm beeping after the engine stops
        DisposeMicChain();
        DisposeCable();

        _player?.Dispose();
        _player = null;
        _mixer = null;
        _gate = null;
    }

    /// <summary>Stop, unwire, and dispose the capture and its passthrough. Safe to call twice.</summary>
    private void DisposeMicChain()
    {
        if (_capture is not null)
        {
            _capture.StateChanged -= OnCaptureStateChanged;
            _capture.Stop();
            _capture.Dispose();
            _capture = null;
        }

        if (_passthrough is not null)
        {
            _passthrough.Drift -= OnDrift;
            _mixer?.RemoveMixerInput(_passthrough.Output);
            _passthrough.Dispose();
            _passthrough = null;
        }
    }

    /// <summary>Stop, unwire, and dispose the cable render. Safe to call twice.</summary>
    private void DisposeCable()
    {
        if (_cableRender is null)
            return;

        _cableRender.StateChanged -= OnCableStateChanged;
        _cableRender.Stop();
        _cableRender.Dispose();
        _cableRender = null;
    }

    public void Dispose()
    {
        TeardownGraph();
        _queue.Dispose();
    }
}
