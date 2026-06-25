using System.Diagnostics;
using AdaVoice.Audio;
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Playback;
using AdaVoice.Audio.Recording;
using AdaVoice.Audio.Setup;
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
    private const string DefaultCategoryId = Category.DefaultId;

    private readonly WasapiAudioOptions _options;
    private RecorderOptions _recorderOptions; // re-set when calibration changes the mic reference
    private readonly Action<string> _log;
    private readonly WasapiDeviceFactory _factory;
    private readonly WasapiDeviceMonitor _monitor;
    private readonly AudioEngine _engine;
    private readonly Thread _controlThread;
    private readonly string _dataRoot;
    private readonly PhraseLibraryService _library;
    private readonly LibraryArchiveService _archive;
    private readonly JsonSettingsRepository _settingsRepository;
    private Settings _settings;
    private volatile bool _running;

    // Recording state (only touched from the UI/driver thread).
    private Recorder? _recorder;
    private IAudioCaptureDevice? _recordingCapture;

    public EngineHost(WasapiAudioOptions options, Action<string>? log = null, RecorderOptions? recorderOptions = null)
    {
        _options = options;
        _recorderOptions = recorderOptions ?? new RecorderOptions();
        _log = log ?? (_ => { });

        // Settings load first: the engine's duck level/ramp are fixed when the phrase player is built.
        _dataRoot = AdaVoicePaths.DefaultRoot;
        _settingsRepository = new JsonSettingsRepository(_dataRoot);
        _settings = _settingsRepository.Load();
        // The wizard-calibrated mic reference (if any) drives the recorder's loudness-match.
        _recorderOptions = _recorderOptions with { ReferenceRms = _settings.MicReferenceRms };

        var clock = new SystemEngineClock();
        _factory = new WasapiDeviceFactory(options);
        _monitor = new WasapiDeviceMonitor();
        _engine = new AudioEngine(_factory, clock, PlayerOptionsFromSettings());

        _engine.Events += OnEngineEvent;
        _monitor.DeviceChanged += OnDeviceChanged;

        _controlThread = new Thread(ControlLoop) { Name = "AudioEngineControl", IsBackground = true };

        var backup = new BackupService(_dataRoot);
        var repository = new JsonPhraseRepository(_dataRoot, backup.TryReadLatestLibrary);
        _library = new PhraseLibraryService(
            repository,
            name => File.Exists(AdaVoicePaths.AudioPath(_dataRoot, name)));
        _archive = new LibraryArchiveService(_dataRoot, repository);

        var detail = _library.LoadDetail is null ? "" : $" — {_library.LoadDetail}";
        _log($"library: {_library.Phrases.Count} phrase(s), status={_library.LoadStatus}{detail}, " +
             $"{_library.BrokenPhraseIds.Count} broken at {_dataRoot}");
        if (_library.LoadStatus == LibraryLoadStatus.Corrupt)
            _log("WARNING: library.json was corrupt and quarantined; started with an empty library (your takes are not lost — see the library.corrupt-*.json file).");
        else if (_library.LoadStatus == LibraryLoadStatus.RecoveredFromBackup)
            _log("NOTE: library.json was corrupt and was restored from the newest daily backup (the corrupt file was kept as library.corrupt-*.json).");

        // Daily backup, after the load so it captures a good (or just-recovered) state. Best-effort.
        var created = backup.EnsureDailyBackup(DateOnly.FromDateTime(DateTime.Now));
        if (created is not null)
            _log($"backup: created {Path.GetFileName(created)}");

        _log($"monitor: {MonitorDescription()}; ducking: {_settings.MicDuckDb:F0} dB over {_settings.DuckRampMs} ms");
    }

    /// <summary>Human-readable name of the device previews play to: the configured monitor, or the OS
    /// default output when none is set.</summary>
    public string MonitorDescription() =>
        _settings is { MonitorEnabled: true, MonitorDeviceName: { } name } ? $"'{name}'" : "OS default output";

    private PhrasePlayerOptions PlayerOptionsFromSettings() => new()
    {
        DuckGain = RampGain.DbToLinear(_settings.MicDuckDb),
        DuckRampMs = _settings.DuckRampMs,
    };

    /// <summary>Run the setup environment checks against the live audio devices (cable present + at
    /// 48 kHz, default output is not the cable, a mic is present).</summary>
    public IReadOnlyList<EnvironmentCheck> RunEnvironmentChecks() =>
        new EnvironmentChecks(new WasapiEnvironmentProbe()).Run(_options.CableName, _options.MicName);

    /// <summary>Voice-calibration step: record <paramref name="seconds"/> of the mic, measure the
    /// reference level, and on success persist it so the recorder loudness-matches future takes to it
    /// (no restart needed). Returns the result, including a too-quiet retry message.</summary>
    public CalibrationResult Calibrate(int seconds = 5)
    {
        var capture = _factory.CreateCapture(DeviceRole.Mic);
        try
        {
            var recorder = new Recorder(capture, _recorderOptions);
            recorder.Start();
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
            var take = recorder.Stop();

            var result = VoiceCalibration.FromTrimmedSamples(take.Samples);
            if (result.Ok)
            {
                _settings = _settings with { MicReferenceRms = result.MicReferenceRms };
                _settingsRepository.Save(_settings);
                _recorderOptions = _recorderOptions with { ReferenceRms = _settings.MicReferenceRms };
            }

            return result;
        }
        finally
        {
            capture.Dispose();
        }
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

    /// <summary>Export the library (metadata + active phrase WAVs) to a zip. Returns the path written.</summary>
    public string ExportLibrary(string destinationZipPath)
    {
        _archive.Export(destinationZipPath);
        return destinationZipPath;
    }

    /// <summary>Import a library archive (merge or replace), then refresh the in-session library so the
    /// change is visible without a restart.</summary>
    public ImportResult ImportLibrary(string sourceZipPath, ImportMode mode)
    {
        var result = _archive.Import(sourceZipPath, mode);
        if (result.Success)
            _library.Reload();
        return result;
    }

    /// <summary>Load a catalogued phrase from disk and preview it. Returns an error message, or null
    /// on success.</summary>
    public string? PreviewEntry(PhraseEntry entry)
    {
        var path = AdaVoicePaths.AudioPath(_dataRoot, entry.FileName);
        if (!File.Exists(path))
            return $"missing audio file: {entry.FileName}";

        return Preview(WavFile.Load(path), entry.GainDb);
    }

    /// <summary>Choose, save, and report the monitor device previews play to. A null or blank name
    /// clears the choice (previews go to the OS default output).</summary>
    public void SetMonitorDevice(string? nameSubstring)
    {
        var name = string.IsNullOrWhiteSpace(nameSubstring) ? null : nameSubstring.Trim();
        _settings = _settings with { MonitorDeviceName = name, MonitorEnabled = name is not null };
        _settingsRepository.Save(_settings);
        _log($"monitor set to {MonitorDescription()}");
    }

    /// <summary>
    /// Play samples to the monitor device (the configured output, else the OS default), applying
    /// <paramref name="gainDb"/>. Refuses if that device is the cable — preview must never reach the
    /// call (decision #11). Blocks until playback finishes. Returns an error message, or null on success.
    /// </summary>
    public string? Preview(float[] samples, double gainDb)
    {
        var device = ResolveMonitorDevice();

        // Cardinal rule: never feed the take toward the call. If the monitor resolves to the cable,
        // refuse rather than play.
        if (device.FriendlyName.Contains(_options.CableName, StringComparison.OrdinalIgnoreCase))
        {
            device.Dispose();
            return "the preview output is the cable — pick a different monitor (or default) playback device";
        }

        _log($"preview → {device.FriendlyName}"); // so the operator can see which device it played to

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

    /// <summary>The output previews play to: the configured monitor device, falling back to the OS
    /// default output if none is set or the chosen one is not currently present.</summary>
    private MMDevice ResolveMonitorDevice() =>
        _settings is { MonitorEnabled: true, MonitorDeviceName: { } name }
            ? WasapiDevices.FindByName(DataFlow.Render, name) ?? WasapiDevices.DefaultRender()
            : WasapiDevices.DefaultRender();

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
