using System.Diagnostics;
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Tests.Engine.Fakes;
using AdaVoice.Audio.Wasapi;

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
