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
/// engine's single control loop on a dedicated thread. The WPF app reuses this class; the console
/// <c>Program</c> around it is a thin dev harness. Logging is a plain callback, so this class
/// depends on no logging library.
/// </summary>
public sealed class EngineHost : IDisposable, IPlaybackHost, IRecorderHost, ISettingsHost, ILibraryHost, ISetupHost
{
    private const string DefaultCategoryId = Category.DefaultId;

    private readonly WasapiAudioOptions _options;
    private RecorderOptions _recorderOptions; // re-set when calibration changes the mic reference
    private readonly Action<string> _log;
    private readonly IAudioDeviceFactory _factory;
    private readonly IDeviceMonitor _monitor;
    private readonly AudioEngine _engine;
    private readonly Thread _controlThread;
    private readonly string _dataRoot;
    private readonly PhraseLibraryService _library;
    private readonly LibraryArchiveService _archive;
    private readonly JsonSettingsRepository _settingsRepository;
    private readonly string? _settingsWarning;
    private Settings _settings;
    private volatile bool _running;

    // Recording state (only touched from the UI/driver thread).
    private Recorder? _recorder;
    private IAudioCaptureDevice? _recordingCapture;

    // The in-flight headphone preview (if any). Preview() runs on a background thread and blocks
    // until playback ends; StopPreview() is called from the UI thread, hence the lock.
    private readonly object _previewLock = new();
    private WasapiRenderDevice? _previewRender;

    /// <param name="factory">Device factory override — tests inject a fake; null = real WASAPI.</param>
    /// <param name="monitor">Device monitor override — tests inject a fake; null = real COM monitor.</param>
    /// <param name="clock">Engine clock override — tests inject a manual clock; null = system clock.</param>
    /// <param name="dataRoot">Data root override — tests use a temp dir; null = %LOCALAPPDATA%\AdaVoice.</param>
    public EngineHost(WasapiAudioOptions options, Action<string>? log = null, RecorderOptions? recorderOptions = null,
        IAudioDeviceFactory? factory = null, IDeviceMonitor? monitor = null, IEngineClock? clock = null,
        string? dataRoot = null)
    {
        _options = options;
        _recorderOptions = recorderOptions ?? new RecorderOptions();
        _log = log ?? (_ => { });

        // Settings load first: the engine's duck level/ramp are fixed when the phrase player is built.
        _dataRoot = dataRoot ?? AdaVoicePaths.DefaultRoot;
        _settingsRepository = new JsonSettingsRepository(_dataRoot);
        _settings = _settingsRepository.Load();
        if (_settingsRepository.LoadReplacedCorruptFile)
            _settingsWarning =
                "Your saved settings could not be read and were reset to defaults — including the " +
                "microphone calibration. Re-run setup calibration so phrases play at the right level.";
        // The wizard-calibrated mic reference (if any) drives the recorder's loudness-match.
        _recorderOptions = _recorderOptions with { ReferenceRms = _settings.MicReferenceRms };

        _factory = factory ?? new WasapiDeviceFactory(options);
        _monitor = monitor ?? new WasapiDeviceMonitor();
        _engine = new AudioEngine(_factory, clock ?? new SystemEngineClock(), PlayerOptionsFromSettings());

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
        // Don't log the data-root path: it contains the Windows username, and this log file already
        // lives in that folder, so the path adds nothing but PII to a log an operator might share
        // for support (security scan 2026-07-12 finding 7).
        _log($"library: {_library.Phrases.Count} phrase(s), status={_library.LoadStatus}{detail}, " +
             $"{_library.BrokenPhraseIds.Count} broken");
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
        ReplaceOnRetrigger = _settings.ReplaceOnRetrigger,
    };

    /// <summary>Run the setup environment checks against the live audio devices (cable present + at
    /// 48 kHz, default output is not the cable, a mic is present).</summary>
    public IReadOnlyList<EnvironmentCheck> RunEnvironmentChecks() =>
        new EnvironmentChecks(new WasapiEnvironmentProbe()).Run(_options.CableName, _options.MicName);

    /// <summary>Voice-calibration step: record <paramref name="seconds"/> of the mic, measure the
    /// reference level, and on success persist it so the recorder loudness-matches future takes to it
    /// (no restart needed). Returns the result, including a too-quiet retry message. If the engine
    /// is Live, the calibration runs OFF AIR (and restores afterwards) — the person on the call
    /// must never hear the calibration speech (review M2, same rule as recording, decision #11).</summary>
    public CalibrationResult Calibrate(int seconds = 5)
    {
        if (_recorder is not null)
            return new CalibrationResult(false, 0, "A recording is in progress — stop it first.");

        var wasLive = State == EngineState.Live;
        if (wasLive)
        {
            EnterOffAir();
            if (!WaitForState(EngineState.OffAir, TimeSpan.FromSeconds(2)))
            {
                ExitOffAir(); // a late transition must not strand the engine OFF AIR
                return new CalibrationResult(false, 0, "Could not pause the call feed — try again.");
            }
        }

        try
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
        finally
        {
            if (wasLive)
                ExitOffAir();
        }
    }

    public EngineState State => _engine.State;

    /// <summary>The catalogued phrases, in stored order.</summary>
    public IReadOnlyList<PhraseEntry> Phrases => _library.Phrases;

    // ---- ILibraryHost (the library read-model + edits the Board makes) --------------------------

    public IReadOnlyList<Category> Categories => _library.Categories;
    public IReadOnlyList<TagInfo> Tags => _library.Tags;
    public IReadOnlyList<Conversation> Conversations => _library.Conversations;
    public IReadOnlyList<string> BrokenPhraseIds => _library.BrokenPhraseIds;
    public IReadOnlyList<string> BrokenVersionIds => _library.BrokenVersionIds;

    /// <summary>False while a mutator would refuse (see <see cref="PhraseLibraryService.IsWritable"/>)
    /// — dialogs gate edit controls on this so a refused edit is visible as "disabled", not a bound
    /// setter's exception swallowed by WPF's binding engine (review finding 9).</summary>
    public bool IsWritable => _library.IsWritable;

    /// <summary>Maps the load status to operator text. Mirrors the log warnings in the constructor,
    /// but reaches the board — an operator never reads the log.</summary>
    public string? LibraryWarning => _library.LoadStatus switch
    {
        LibraryLoadStatus.ReadError =>
            "Your phrase library could not be read (another program may be holding the file). " +
            "Your phrases are safe, but changes are disabled — restart AdaVoice to try again.",
        LibraryLoadStatus.Corrupt =>
            "Your phrase library file was unreadable and has been set aside, so the board starts empty. " +
            "Your recordings are still on disk.",
        LibraryLoadStatus.RecoveredFromBackup =>
            "Your phrase library was restored from the latest daily backup — very recent changes may be missing.",
        _ => null,
    };

    public string? SettingsWarning => _settingsWarning;

    public PhraseEntry? SetPhraseTitle(string phraseId, string title) => _library.SetPhraseTitle(phraseId, title);
    public PhraseEntry? SetPhraseCategory(string phraseId, string categoryId) => _library.SetPhraseCategory(phraseId, categoryId);
    public PhraseEntry? SetPhraseTags(string phraseId, IEnumerable<string> tags) => _library.SetPhraseTags(phraseId, tags);

    public PhraseEntry? DeletePhraseVersion(string phraseId, string versionId) =>
        _library.DeletePhraseVersion(phraseId, versionId, (current, orphan) =>
        {
            var src = AdaVoicePaths.AudioPath(_dataRoot, current);
            if (File.Exists(src))
                File.Move(src, AdaVoicePaths.AudioPath(_dataRoot, orphan), overwrite: true);
        });

    public PhraseEntry? SetPhraseVersionLabel(string phraseId, string versionId, string label) =>
        _library.SetPhraseVersionLabel(phraseId, versionId, label);

    public Category AddCategory(string name, string color) => _library.AddCategory(name, color);
    public Category? UpdateCategory(string id, string name, string color) => _library.UpdateCategory(id, name, color);
    public bool DeleteCategory(string id) => _library.DeleteCategory(id);

    public Conversation AddConversation(string name) => _library.AddConversation(name);
    public Conversation? RenameConversation(string id, string name) => _library.RenameConversation(id, name);
    public bool DeleteConversation(string id) => _library.DeleteConversation(id);
    public Conversation? SetConversationPhrases(string id, IReadOnlyList<string> phraseIds) =>
        _library.SetConversationPhrases(id, phraseIds);
    public Conversation? SetConversationUseRandomVersion(string id, bool useRandomVersion) =>
        _library.SetConversationUseRandomVersion(id, useRandomVersion);

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

    /// <summary>Stops a phrase playing to the call and/or an in-progress headphone preview — the
    /// operator's one STOP button silences whatever is currently audible.</summary>
    public void StopPhrase()
    {
        _engine.StopPhrase();
        StopPreview();
    }

    public void EnterOffAir() => _engine.EnterOffAir();
    public void ExitOffAir() => _engine.ExitOffAir();

    /// <summary>Raised when the engine state changes. Fires on the engine control thread — a UI
    /// handler must marshal to its own thread (e.g. the WPF Dispatcher).</summary>
    public event EventHandler<EngineStateChangedEventArgs>? StateChanged;

    /// <summary>Raised with the playing phrase's id (null when playback stops). Fires off the UI
    /// thread — a UI handler must marshal.</summary>
    public event EventHandler<string?>? PlayingPhraseChanged;

    /// <summary>Load a catalogued phrase from disk, apply its loudness-match gain, and play it toward
    /// the call (the cable). The engine routes Play to the cable only when Live, so this is a no-op
    /// otherwise. When <paramref name="version"/> is given, that take's file and gain are used instead
    /// of the entry's own — the phrase id used downstream (for <see cref="PlayingPhraseChanged"/>) is
    /// always the entry's, regardless of which take played. Returns an error message if nothing was
    /// played (not Live, or the audio file is missing), or null on success — mirrors
    /// <see cref="PreviewEntry"/> so a caller can surface the drop instead of silence.</summary>
    public string? PlayEntry(PhraseEntry entry, PhraseVersion? version = null)
    {
        if (State != EngineState.Live)
        {
            // The engine routes Play to the cable only when Live; surface the drop instead of silence.
            var message = $"engine is {State}, not Live — press Start (and be ON AIR)";
            _log($"cannot play {entry.Id}: {message}");
            return message;
        }

        var fileName = version?.FileName ?? entry.FileName;
        var gainDb = version?.GainDb ?? entry.GainDb;
        var path = AdaVoicePaths.AudioPath(_dataRoot, fileName);
        if (!File.Exists(path))
        {
            var message = $"missing audio file: {fileName}";
            _log($"cannot play {entry.Id}: {message}");
            return message;
        }

        var samples = WavFile.Load(path);
        var gain = RampGain.DbToLinear(gainDb);
        for (var i = 0; i < samples.Length; i++)
            samples[i] *= gain;

        Play(new Phrase(entry.Id, samples));
        return null;
    }

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
        try
        {
            if (!WaitForState(EngineState.OffAir, TimeSpan.FromSeconds(2)))
            {
                // The enter may still land late on the control thread; queue the exit either
                // way so a slow transition can never strand the engine OFF AIR (review M1).
                ExitOffAir();
                return false;
            }

            _recordingCapture = _factory.CreateCapture(DeviceRole.Mic);
            _recorder = new Recorder(_recordingCapture, _recorderOptions);
            _recorder.Start();
            return true;
        }
        catch
        {
            // e.g. the mic vanished between OFF AIR and CreateCapture. Undo the OFF AIR before
            // rethrowing — a failed recording start must never leave the operator muted (M1).
            _recordingCapture?.Dispose();
            _recordingCapture = null;
            _recorder = null;
            ExitOffAir();
            throw;
        }
    }

    /// <summary>Stop the current take, restore the live state, and return the processed result.</summary>
    public RecordingResult? StopRecording()
    {
        if (_recorder is null)
            return null;

        try
        {
            return _recorder.Stop();
        }
        finally
        {
            // Going back on air must happen no matter what — a dead capture's Dispose throwing
            // must not leave the operator muted (M1).
            try
            {
                _recordingCapture!.Dispose();
            }
            catch (Exception ex)
            {
                _log($"recording capture dispose failed: {ex.Message}");
            }

            _recorder = null;
            _recordingCapture = null;
            ExitOffAir();
        }
    }

    /// <summary>Catalogue a recorded take: write its WAV under the data root, then add the metadata
    /// (WAV first, so a failed write catalogues nothing). Returns the stored entry.</summary>
    public PhraseEntry SaveTake(RecordingResult result, string title) =>
        _library.Add(title, DefaultCategoryId, result.DurationMs, result.GainDb,
            fileName => WavFile.Save(AdaVoicePaths.AudioPath(_dataRoot, fileName), result.Samples));

    /// <summary>Catalogue a recorded take as a new version of an existing phrase (WAV written before
    /// metadata, same discipline as <see cref="SaveTake"/>). Returns the updated entry, or null if the
    /// phrase id is unknown.</summary>
    public PhraseEntry? SaveTakeAsVersion(RecordingResult result, string phraseId, string label) =>
        _library.AddPhraseVersion(phraseId, label, result.DurationMs, result.GainDb,
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

    /// <summary>Export the library (metadata + active phrase WAVs) to a zip. Version recordings are
    /// not included (v1 limitation) — logged when any are dropped, and the count is returned so a
    /// caller can tell the operator (review finding 2: this used to be logged only, with no on-screen
    /// trace of the drop).</summary>
    public int ExportLibrary(string destinationZipPath)
    {
        var droppedVersions = _archive.Export(destinationZipPath);
        if (droppedVersions > 0)
            _log($"export: dropped {droppedVersions} version recording(s) — not included in exports (v1 limitation)");
        return droppedVersions;
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

    /// <summary>Load one version of a phrase from disk and preview it. Returns an error message, or
    /// null on success.</summary>
    public string? PreviewVersion(PhraseVersion version)
    {
        var path = AdaVoicePaths.AudioPath(_dataRoot, version.FileName);
        if (!File.Exists(path))
            return $"missing audio file: {version.FileName}";

        return Preview(WavFile.Load(path), version.GainDb);
    }

    // ---- ISettingsHost --------------------------------------------------------------------------

    /// <summary>The current mic-duck level in dB.</summary>
    public double MicDuckDb => _settings.MicDuckDb;

    /// <summary>Apply a new duck level live (engine + in-memory settings) without writing to disk.</summary>
    public void SetMicDuckDb(double db)
    {
        db = Math.Clamp(db, -40, 0);
        _settings = _settings with { MicDuckDb = db };
        _engine.SetDuckLevel(RampGain.DbToLinear((float)db), _settings.DuckRampMs);
    }

    /// <summary>Persist the current settings to disk (call when a slider drag finishes).</summary>
    public void SaveSettings() => _settingsRepository.Save(_settings);

    /// <summary>The window's saved size and position, or null if any coordinate was never saved.</summary>
    public WindowPlacement? WindowPlacement =>
        _settings is { WindowWidth: { } w, WindowHeight: { } h, WindowLeft: { } left, WindowTop: { } top }
            ? new WindowPlacement(w, h, left, top)
            : null;

    /// <summary>Remember the window's size and position and persist it (called when the window closes).</summary>
    public void SaveWindowPlacement(double width, double height, double left, double top)
    {
        _settings = _settings with
        {
            WindowWidth = width,
            WindowHeight = height,
            WindowLeft = left,
            WindowTop = top,
        };
        _settingsRepository.Save(_settings);
    }

    /// <summary>True once the setup wizard has been completed at least once.</summary>
    public bool WizardCompleted => _settings.WizardCompleted;

    /// <summary>Mark the setup wizard completed and persist immediately.</summary>
    public void MarkWizardCompleted()
    {
        _settings = _settings with { WizardCompleted = true };
        _settingsRepository.Save(_settings);
    }

    /// <summary>Whether the Board window should stay always-on-top.</summary>
    public bool AlwaysOnTop => _settings.AlwaysOnTop;

    /// <summary>Remember the always-on-top preference in memory. Does not persist.</summary>
    public void SetAlwaysOnTop(bool value) => _settings = _settings with { AlwaysOnTop = value };

    /// <summary>Whether a new phrase trigger replaces the one currently playing.</summary>
    public bool ReplaceOnRetrigger => _settings.ReplaceOnRetrigger;

    /// <summary>Remember the retrigger preference in memory. Does not persist. Takes effect on the
    /// next restart (read by <see cref="PlayerOptionsFromSettings"/> at construction).</summary>
    public void SetReplaceOnRetrigger(bool value) => _settings = _settings with { ReplaceOnRetrigger = value };

    /// <summary>The UI language code.</summary>
    public string Language => _settings.Language;

    /// <summary>Remember the language preference in memory. Does not persist.</summary>
    public void SetLanguage(string code) => _settings = _settings with { Language = code };

    /// <summary>Export the library to a zip — thin delegation to the already-existing
    /// <see cref="ExportLibrary"/> (used today by the console host). Returns the number of version
    /// recordings that were not included (0 if none).</summary>
    public int Export(string destinationZipPath) => ExportLibrary(destinationZipPath);

    /// <summary>Import a library archive — thin delegation to the already-existing
    /// <see cref="ImportLibrary"/> (used today by the console host), which already reloads the
    /// in-session library on success.</summary>
    public ImportResult Import(string sourceZipPath, ImportMode mode) => ImportLibrary(sourceZipPath, mode);

    /// <summary>The date of the newest daily backup, or null if none exist yet.</summary>
    public DateOnly? LastBackupDate => new BackupService(_dataRoot).LatestBackupDate();

    /// <summary>Open the backups folder in the OS file explorer.</summary>
    public void OpenBackupFolder() =>
        Process.Start(new ProcessStartInfo(AdaVoicePaths.BackupsDir(_dataRoot)) { UseShellExecute = true });

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

        // AudioClient is a fresh RCW per access — dispose it (one leak per preview otherwise).
    int deviceRate;
    using (var audioClient = device.AudioClient)
        deviceRate = audioClient.MixFormat.SampleRate;
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

        lock (_previewLock)
            _previewRender = render;
        try
        {
            render.Init(source);
            render.Start();

            var durationMs = samples.Length * 1000L / AudioFormats.SampleRate;
            done.Wait(TimeSpan.FromMilliseconds(durationMs + 1000)); // backstop in case the tail is delayed, or StopPreview() cuts it short
            render.Stop();
            return null;
        }
        finally
        {
            lock (_previewLock)
            {
                if (ReferenceEquals(_previewRender, render))
                    _previewRender = null;
            }
        }
    }

    /// <summary>Stop the in-flight preview, if any — <see cref="Preview"/>'s blocking wait unblocks as
    /// soon as the render device reports Stopped.</summary>
    public void StopPreview()
    {
        WasapiRenderDevice? render;
        lock (_previewLock)
            render = _previewRender;
        render?.Stop();
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

    private void OnEngineEvent(object? sender, EngineEvent e)
    {
        // PhraseChanged can fire on the audio render thread (under the mixer lock) on a natural end —
        // re-raise it without logging, so no file I/O ever touches the audio path. The BeginInvoke the
        // UI handler does is lightweight. State/drift/rebuild events fire on the control thread and log.
        if (e is EngineEvent.PhraseChanged p)
        {
            PlayingPhraseChanged?.Invoke(this, p.PhraseId);
            return;
        }

        _log(Describe(e));
        if (e is EngineEvent.StateChanged s)
            StateChanged?.Invoke(this, new EngineStateChangedEventArgs(s.State, s.Error));
    }

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
