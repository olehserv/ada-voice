using System.Diagnostics;
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Audio.Tests.Engine.Fakes;
using AdaVoice.Audio.Wasapi;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Host.Tests;

/// <summary>
/// EngineHost against fakes and a temp data root — the seam injection added for review H11.
/// These cover host-owned rules that no other layer sees (library warnings, recording flow).
/// </summary>
public class EngineHostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-host-" + Guid.NewGuid().ToString("N"));

    private EngineHost NewHost(FakeDeviceFactory? factory = null) =>
        new(new WasapiAudioOptions(), log: null, recorderOptions: null,
            factory: factory ?? new FakeDeviceFactory(),
            monitor: new FakeDeviceMonitor(),
            clock: new ManualEngineClock(),
            dataRoot: _root);

    [Fact]
    public void Fresh_data_root_loads_an_empty_library_with_no_warning()
    {
        using var host = NewHost();

        Assert.Null(host.LibraryWarning);
        Assert.Empty(host.Phrases);
    }

    // StopPreview() with an active preview needs a real render device (WASAPI), so it is not covered
    // here — this only guards the no-op contract when there is nothing to stop.
    [Fact]
    public void Stop_preview_is_a_no_op_when_nothing_is_previewing()
    {
        using var host = NewHost();

        var ex = Record.Exception(() => host.StopPreview());

        Assert.Null(ex);
    }

    [Fact]
    public void A_corrupt_settings_file_surfaces_a_settings_warning()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{ not valid json");

        using var host = NewHost();

        Assert.NotNull(host.SettingsWarning);
    }

    [Fact]
    public void A_clean_data_root_has_no_settings_warning()
    {
        using var host = NewHost();

        Assert.Null(host.SettingsWarning);
    }

    [Fact]
    public void A_locked_library_file_surfaces_a_read_error_warning()
    {
        // Seed a real library file, then hold it exclusively (like an AV scan would).
        Directory.CreateDirectory(_root);
        var libraryPath = Path.Combine(_root, "library.json");
        File.WriteAllText(libraryPath, """{"version":1,"categories":[],"phrases":[]}""");
        using var padlock = new FileStream(libraryPath, FileMode.Open, FileAccess.Read, FileShare.None);

        using var host = NewHost();

        Assert.NotNull(host.LibraryWarning);
        Assert.Contains("could not be read", host.LibraryWarning);
    }

    // ---- Recording / calibration state safety (review M1/M2) ----------------------------------

    // M1: a failure between OFF AIR and the capture opening (mic vanished) must not leave the
    // operator muted — the host restores the live state before rethrowing.
    [Fact]
    public async Task Failed_recording_start_returns_the_engine_on_air()
    {
        var factory = new FakeDeviceFactory();
        using var host = NewHost(factory);
        host.Start();
        await WaitFor(() => host.State == EngineState.Live);

        factory.FailNext(DeviceRole.Mic, transient: true); // the recording capture fails to open
        Assert.ThrowsAny<Exception>(() => host.TryStartRecording());

        await WaitFor(() => host.State == EngineState.Live); // restored, not stranded OFF AIR
    }

    // M2: calibration speech must never reach the call — while Live it runs OFF AIR and restores.
    [Fact]
    public async Task Calibration_while_live_goes_off_air_and_restores()
    {
        var factory = new FakeDeviceFactory();
        using var host = NewHost(factory);
        host.Start();
        await WaitFor(() => host.State == EngineState.Live);

        var seen = new List<EngineState>();
        host.StateChanged += (_, e) => seen.Add(e.State);

        host.Calibrate(seconds: 0); // 0 s: no real wait, the OFF AIR dance still runs

        await WaitFor(() => host.State == EngineState.Live);
        Assert.Contains(EngineState.OffAir, seen); // the call feed was paused during capture
    }

    // ---- PlayEntry version-vs-primary selection (review finding 1 / 6) ------------------------

    /// <summary>Review finding 6: the version-vs-primary file selection in <c>PlayEntry</c> had zero
    /// coverage — a swapped `??` would send the wrong take (or the wrong loudness) into a live call,
    /// invisibly. Both files are left missing so this is provable without a real WAV: the returned
    /// message names whichever file was actually chosen.</summary>
    [Fact]
    public async Task PlayEntry_uses_the_version_file_not_the_primary()
    {
        using var host = NewHost();
        host.Start();
        await WaitFor(() => host.State == EngineState.Live);
        var entry = new PhraseEntry { Id = "p-1", FileName = "primary.wav", GainDb = -3 };
        var version = new PhraseVersion { Id = "pv-1", FileName = "version.wav", GainDb = -6 };

        var error = host.PlayEntry(entry, version);

        Assert.Contains("version.wav", error);
        Assert.DoesNotContain("primary.wav", error ?? "");
    }

    [Fact]
    public async Task PlayEntry_falls_back_to_the_primary_when_version_is_null()
    {
        using var host = NewHost();
        host.Start();
        await WaitFor(() => host.State == EngineState.Live);
        var entry = new PhraseEntry { Id = "p-1", FileName = "primary.wav" };

        var error = host.PlayEntry(entry, version: null);

        Assert.Contains("primary.wav", error);
    }

    // ---- Version WAV lifecycle (review finding 6) ----------------------------------------------

    /// <summary>Review finding 6: the version WAV write (SaveTakeAsVersion) and orphan-move
    /// (DeletePhraseVersion) wiring in EngineHost had no test on a real data root — a wrong
    /// AudioPath join or move target would lose or orphan an irreplaceable take invisibly.</summary>
    [Fact]
    public void SaveTakeAsVersion_writes_the_version_wav_then_DeletePhraseVersion_orphans_it()
    {
        using var host = NewHost();
        var take = new RecordingResult(new float[10], GainDb: -3, DurationMs: 500, PeakDbfs: -6);
        var entry = host.SaveTake(take, "Greeting");

        var updated = host.SaveTakeAsVersion(take, entry.Id, "Alt take");

        Assert.NotNull(updated);
        var version = Assert.Single(updated!.Versions);
        var versionPath = AdaVoicePaths.AudioPath(_root, version.FileName);
        Assert.True(File.Exists(versionPath));

        host.DeletePhraseVersion(entry.Id, version.Id);

        Assert.False(File.Exists(versionPath)); // never destroyed — renamed, not deleted
        Assert.True(File.Exists(AdaVoicePaths.AudioPath(_root, "deleted-" + version.FileName)));
    }

    [Fact]
    public void PlayEntry_refuses_and_returns_a_reason_when_the_engine_is_not_live()
    {
        using var host = NewHost(); // never started — still Stopped
        var entry = new PhraseEntry { Id = "p-1", FileName = "primary.wav" };

        var error = host.PlayEntry(entry);

        Assert.NotNull(error);
        Assert.Contains("not Live", error);
    }

    [Fact]
    public async Task Calibration_refuses_while_a_recording_is_in_progress()
    {
        var factory = new FakeDeviceFactory();
        using var host = NewHost(factory);
        host.Start();
        await WaitFor(() => host.State == EngineState.Live);
        Assert.True(host.TryStartRecording());

        var result = host.Calibrate(seconds: 0);

        Assert.False(result.Ok);
        Assert.Contains("recording", result.Message, StringComparison.OrdinalIgnoreCase);
        host.StopRecording();
    }

    /// <summary>Engine state changes land on the host control thread — poll briefly.</summary>
    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(5);
        Assert.True(condition(), "condition not reached within the timeout");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
