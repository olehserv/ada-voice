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

    public void Dispose()
    {
        _watchdog?.Dispose();
        _capture?.Dispose();
        _cableRender?.Dispose();
        _passthrough?.Dispose();
        _player?.Dispose();
        _queue.Dispose();
    }
}
