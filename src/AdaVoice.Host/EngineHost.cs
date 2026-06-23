using System.Diagnostics;
using AdaVoice.Audio;
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Playback;
using AdaVoice.Audio.Recording;
using AdaVoice.Audio.Storage;
using AdaVoice.Audio.Wasapi;
using AdaVoice.Core;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// Both AdaVoice and NAudio define DeviceState; the seam uses ours.
using DeviceState = AdaVoice.Audio.Abstractions.DeviceState;

namespace AdaVoice.Host;

/// <summary>
/// Composition root for the audio engine. It builds the real WASAPI device factory, the device
/// monitor, and the system clock, wires them into an <see cref="AudioEngine"/>, and runs the
/// engine's single control loop on a dedicated thread. The future WPF app reuses this class; only
/// the console <c>Program</c> around it is throwaway. Logging is a plain callback, so this class
/// depends on no logging library.
/// </summary>
public sealed class EngineHost : IDisposable
{
    private const string DefaultCategoryId = "c-default";

    private readonly WasapiAudioOptions _options;
    private readonly RecorderOptions _recorderOptions;
    private readonly Action<string> _log;
    private readonly WasapiDeviceFactory _factory;
    private readonly WasapiDeviceMonitor _monitor;
    private readonly AudioEngine _engine;
    private readonly Thread _controlThread;
    private readonly string _dataRoot;
    private readonly PhraseLibraryService _library;
    private volatile bool _running;

    // Recording state (only touched from the UI/driver thread).
    private Recorder? _recorder;
    private IAudioCaptureDevice? _recordingCapture;

    public EngineHost(WasapiAudioOptions options, Action<string>? log = null, RecorderOptions? recorderOptions = null)
    {
        _options = options;
        _recorderOptions = recorderOptions ?? new RecorderOptions();
        _log = log ?? (_ => { });

        var clock = new SystemEngineClock();
        _factory = new WasapiDeviceFactory(options);
        _monitor = new WasapiDeviceMonitor();
        _engine = new AudioEngine(_factory, clock);

        _engine.Events += OnEngineEvent;
        _monitor.DeviceChanged += OnDeviceChanged;

        _controlThread = new Thread(ControlLoop) { Name = "AudioEngineControl", IsBackground = true };

        _dataRoot = AdaVoicePaths.DefaultRoot;
        _library = new PhraseLibraryService(
            new JsonPhraseRepository(_dataRoot),
            name => File.Exists(AdaVoicePaths.AudioPath(_dataRoot, name)));

        var detail = _library.LoadDetail is null ? "" : $" — {_library.LoadDetail}";
        _log($"library: {_library.Phrases.Count} phrase(s), status={_library.LoadStatus}{detail}, " +
             $"{_library.BrokenPhraseIds.Count} broken at {_dataRoot}");
        if (_library.LoadStatus == LibraryLoadStatus.Corrupt)
            _log("WARNING: library.json was corrupt and quarantined; started with an empty library (your takes are not lost — see the library.corrupt-*.json file).");
    }

    public EngineState State => _engine.State;

    /// <summary>The catalogued phrases, in stored order.</summary>
    public IReadOnlyList<PhraseEntry> Phrases => _library.Phrases;

    public void Start()
    {
        if (!_running)
        {
            _running = true;
            _controlThread.Start();
            _monitor.Start();
        }

        _engine.Start();
    }

    public void Stop() => _engine.Stop();
    public void Play(Phrase phrase) => _engine.Play(phrase);
    public void EnterOffAir() => _engine.EnterOffAir();
    public void ExitOffAir() => _engine.ExitOffAir();

    /// <summary>
    /// Take the engine OFF AIR and start recording the mic on its own capture. Returns false if the
    /// engine could not reach OFF AIR (e.g. not Live yet) — recording is only allowed off air
    /// (decision #11). OFF AIR is processed on the control thread, so we wait for the state.
    /// </summary>
    public bool TryStartRecording()
    {
        if (_recorder is not null)
            return false; // already recording

        EnterOffAir();
        if (!WaitForState(EngineState.OffAir, TimeSpan.FromSeconds(2)))
            return false;

        _recordingCapture = _factory.CreateCapture(DeviceRole.Mic);
        _recorder = new Recorder(_recordingCapture, _recorderOptions);
        _recorder.Start();
        return true;
    }

    /// <summary>Stop the current take, restore the live state, and return the processed result.</summary>
    public RecordingResult? StopRecording()
    {
        if (_recorder is null)
            return null;

        var result = _recorder.Stop();
        _recordingCapture!.Dispose();
        _recorder = null;
        _recordingCapture = null;

        ExitOffAir();
        return result;
    }

    /// <summary>Catalogue a recorded take: write its WAV under the data root, then add the metadata
    /// (WAV first, so a failed write catalogues nothing). Returns the stored entry.</summary>
    public PhraseEntry SaveTake(RecordingResult result, string title) =>
        _library.Add(title, DefaultCategoryId, result.DurationMs, result.GainDb,
            fileName => WavFile.Save(AdaVoicePaths.AudioPath(_dataRoot, fileName), result.Samples));

    /// <summary>Delete a phrase: drop the metadata and rename its WAV to <c>deleted-{id}.wav</c> in
    /// place (never destroyed — design 04 §3). Returns the removed entry, or null if not found.</summary>
    public PhraseEntry? DeleteEntry(PhraseEntry entry) =>
        _library.Delete(entry.Id, (current, orphan) =>
        {
            var src = AdaVoicePaths.AudioPath(_dataRoot, current);
            if (File.Exists(src))
                File.Move(src, AdaVoicePaths.AudioPath(_dataRoot, orphan), overwrite: true);
        });

    /// <summary>Load a catalogued phrase from disk and preview it. Returns an error message, or null
    /// on success.</summary>
    public string? PreviewEntry(PhraseEntry entry)
    {
        var path = AdaVoicePaths.AudioPath(_dataRoot, entry.FileName);
        if (!File.Exists(path))
            return $"missing audio file: {entry.FileName}";

        return Preview(WavFile.Load(path), entry.GainDb);
    }

    /// <summary>
    /// Play samples to the default output (the monitor stand-in), applying <paramref name="gainDb"/>.
    /// Refuses if the default output is the cable — preview must never reach the call (decision #11).
    /// Blocks until playback finishes. Returns an error message, or null on success.
    /// </summary>
    public string? Preview(float[] samples, double gainDb)
    {
        var device = WasapiDevices.DefaultRender();

        // Cardinal rule: never feed the take toward the call. If the OS default output is the cable,
        // refuse rather than play.
        if (device.FriendlyName.Contains(_options.CableName, StringComparison.OrdinalIgnoreCase))
        {
            device.Dispose();
            return "default output is the cable — pick a different playback device to preview";
        }

        var deviceRate = device.AudioClient.MixFormat.SampleRate;
        ISampleProvider source = new PhraseSampleProvider(samples, AudioFormats.Engine, "preview");
        source = new VolumeSampleProvider(source) { Volume = RampGain.DbToLinear(gainDb) };
        if (deviceRate != AudioFormats.SampleRate)
            source = new WdlResamplingSampleProvider(source, deviceRate);

        using var render = new WasapiRenderDevice(device, optOutOfDucking: false);
        using var done = new ManualResetEventSlim(false);
        render.StateChanged += (_, e) =>
        {
            if (e.State is DeviceState.Stopped or DeviceState.Faulted)
                done.Set();
        };

        render.Init(source);
        render.Start();

        var durationMs = samples.Length * 1000L / AudioFormats.SampleRate;
        done.Wait(TimeSpan.FromMilliseconds(durationMs + 1000)); // backstop in case the tail is delayed
        render.Stop();
        return null;
    }

    private bool WaitForState(EngineState target, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (_engine.State == target)
                return true;
            Thread.Sleep(5);
        }

        return _engine.State == target;
    }

    /// <summary>The single thread every command handler runs on. The clock timer and the device
    /// monitor only enqueue; all state transitions happen here.</summary>
    private void ControlLoop()
    {
        while (_running)
        {
            try
            {
                _engine.TryProcessNext(timeoutMs: 100);
            }
            catch (Exception ex)
            {
                // Belt-and-suspenders: no handler exception may ever kill the control thread.
                _log($"control loop error: {ex}");
            }
        }
    }

    private void OnEngineEvent(object? sender, EngineEvent e) => _log(Describe(e));

    private static string Describe(EngineEvent e) => e switch
    {
        EngineEvent.StateChanged s => $"state -> {s.State}{(s.Error is null ? "" : $" ({s.Error})")}",
        EngineEvent.DriftLogged d => $"drift: {d.Kind}",
        EngineEvent.RebuildResult r => $"rebuild {r.Role}: {(r.Success ? "ok" : "failed")} (attempt {r.Attempt})",
        _ => e.ToString() ?? string.Empty,
    };

    private void OnDeviceChanged(object? sender, DeviceChangeEventArgs e)
    {
        // Best-effort mapping (pragmatic v1): classify the device's flow -> role. The engine ignores
        // anything not currently relevant, so a coarse guess is safe. Device-loss recovery does not
        // rely on this (the seam fault callbacks already drive it); this mainly speeds up replug.
        if (RoleOf(e.DeviceId) is { } role)
        {
            _log($"device {e.Kind}: {role}");
            _engine.Post(new EngineCommand.DeviceChanged(role, e.Kind));
        }
    }

    private DeviceRole? RoleOf(string deviceId)
    {
        // A removed device no longer resolves, so we cannot classify it — that is fine, the engine's
        // fault path already handles removal. We mainly need to classify present devices (arrived /
        // new default) for the fast-path rebuild.
        using var device = WasapiDevices.ById(deviceId);
        if (device is null)
            return null;

        return device.DataFlow switch
        {
            DataFlow.Capture when _options.MicName is null
                || device.FriendlyName.Contains(_options.MicName, StringComparison.OrdinalIgnoreCase)
                => DeviceRole.Mic,

            DataFlow.Render when device.FriendlyName.Contains(_options.CableName, StringComparison.OrdinalIgnoreCase)
                => DeviceRole.Cable,

            _ => null,
        };
    }

    public void Dispose()
    {
        // Order matters: stop the loop, then stop the monitor (its COM callbacks Post), then dispose
        // the engine (which disposes the watchdog timer before the queue). This keeps a late Post off
        // a disposed queue; AudioEngine.Post also guards the remaining in-flight race.
        _running = false;
        if (_controlThread.IsAlive)
            _controlThread.Join(TimeSpan.FromSeconds(2));

        _recordingCapture?.Dispose(); // in case we are disposed mid-take

        _monitor.DeviceChanged -= OnDeviceChanged;
        _monitor.Dispose();

        _engine.Events -= OnEngineEvent;
        _engine.Dispose();
    }
}
