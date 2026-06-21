using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Playback;
using AdaVoice.Audio.Tests.Engine.Fakes;
using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests.Engine;

public class AudioEngineTests
{
    private static (AudioEngine engine, FakeDeviceFactory factory, ManualEngineClock clock, List<EngineEvent> events) NewEngine()
    {
        var factory = new FakeDeviceFactory();
        var clock = new ManualEngineClock();
        var engine = new AudioEngine(factory, clock);
        var events = new List<EngineEvent>();
        engine.Events += (_, e) => events.Add(e);
        return (engine, factory, clock, events);
    }

    [Fact]
    public void Start_opens_devices_and_goes_live()
    {
        var (engine, factory, _, events) = NewEngine();

        engine.Start();
        engine.DrainPending();

        Assert.Equal(EngineState.Live, engine.State);
        Assert.NotNull(factory.LastMic);
        Assert.NotNull(factory.LastCable);
        Assert.Contains(events, e => e is EngineEvent.StateChanged { State: EngineState.Live });
    }

    [Fact]
    public void Stop_tears_down_streams_and_goes_stopped()
    {
        var (engine, factory, _, events) = NewEngine();
        engine.Start();
        engine.DrainPending();

        engine.Stop();
        engine.DrainPending();

        Assert.Equal(EngineState.Stopped, engine.State);
        Assert.Equal(DeviceState.Stopped, factory.LastMic!.State);
        Assert.Equal(DeviceState.Stopped, factory.LastCable!.State);
        Assert.Contains(events, e => e is EngineEvent.StateChanged { State: EngineState.Stopped });
    }

    [Fact]
    public void Stop_when_already_stopped_does_nothing()
    {
        var (engine, _, _, events) = NewEngine();

        engine.Stop();
        engine.DrainPending();

        Assert.Equal(EngineState.Stopped, engine.State);
        Assert.DoesNotContain(events, e => e is EngineEvent.StateChanged);
    }

    [Fact]
    public void OffAir_silences_the_cable()
    {
        var (engine, factory, _, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        var mic = factory.LastMic!;
        var cable = factory.LastCable!;

        engine.EnterOffAir();
        engine.DrainPending();
        Assert.Equal(EngineState.OffAir, engine.State);

        mic.Push(TestAudio.Sine(440, 4800));
        cable.Pull(4800);

        Assert.All(cable.Captured, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void ExitOffAir_lets_audio_reach_the_cable_again()
    {
        var (engine, factory, _, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        var mic = factory.LastMic!;
        var cable = factory.LastCable!;

        engine.EnterOffAir();
        engine.DrainPending();
        engine.ExitOffAir();
        engine.DrainPending();
        Assert.Equal(EngineState.Live, engine.State);

        mic.Push(TestAudio.Sine(440, 4800));
        cable.Pull(4800);

        Assert.Contains(cable.Captured, s => s != 0f);
    }

    [Fact]
    public void EnterOffAir_is_ignored_when_not_live()
    {
        var (engine, _, _, _) = NewEngine();

        engine.EnterOffAir();
        engine.DrainPending();

        Assert.Equal(EngineState.Stopped, engine.State);
    }

    [Fact]
    public void Play_while_live_sends_the_phrase_to_the_cable()
    {
        var (engine, factory, _, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        var cable = factory.LastCable!;

        engine.Play(new Phrase("p", TestAudio.Sine(440, 4800)));
        engine.DrainPending();
        cable.Pull(4800);

        Assert.Contains(cable.Captured, s => s != 0f);
    }

    [Fact]
    public void Play_is_ignored_while_off_air()
    {
        var (engine, factory, _, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        var cable = factory.LastCable!;

        engine.EnterOffAir();
        engine.DrainPending();

        engine.Play(new Phrase("p", Enumerable.Repeat(0.5f, 48_000).ToArray()));
        engine.DrainPending();

        engine.ExitOffAir();
        engine.DrainPending();
        cable.Pull(4800);

        // If Play had been honored during OFF AIR, the phrase would now be sounding.
        Assert.All(cable.Captured, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Mic_drift_is_forwarded_as_a_drift_event()
    {
        var (engine, factory, _, events) = NewEngine();
        engine.Start();
        engine.DrainPending();
        var mic = factory.LastMic!;

        // Two seconds of audio pushed back-to-back overflows the 100 ms backlog and forces an
        // overrun, which MicPassthrough reports as drift.
        mic.Push(new float[TestAudio.SampleRate]);
        mic.Push(new float[TestAudio.SampleRate]);

        Assert.Contains(events, e => e is EngineEvent.DriftLogged);
    }

    [Fact]
    public void Capture_fault_goes_degraded_and_sounds_the_alarm()
    {
        var (engine, factory, _, events) = NewEngine();
        engine.Start();
        engine.DrainPending();

        factory.LastMic!.Fault(new InvalidOperationException("mic died"));
        engine.DrainPending();

        Assert.Equal(EngineState.Degraded, engine.State);
        Assert.NotNull(factory.LastAlarm);
        Assert.Equal(DeviceState.Running, factory.LastAlarm!.State);
        Assert.Contains(events, e => e is EngineEvent.StateChanged { State: EngineState.Degraded });
    }

    [Fact]
    public void Watchdog_stall_goes_degraded()
    {
        var (engine, factory, clock, _) = NewEngine();
        engine.Start();
        engine.DrainPending();

        // Never pull the cable, so the gate's last-read stamp goes stale.
        clock.Advance(StallMs + 100);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Degraded, engine.State);
        Assert.NotNull(factory.LastAlarm);
    }

    [Fact]
    public void Degraded_still_holds_when_the_alarm_device_is_gone()
    {
        var (engine, factory, _, _) = NewEngine();
        engine.Start();
        engine.DrainPending();

        factory.FailNext(DeviceRole.Alarm, transient: true);
        factory.LastMic!.Fault(new InvalidOperationException("mic died"));
        engine.DrainPending();

        Assert.Equal(EngineState.Degraded, engine.State); // visual banner still shows
        Assert.Null(factory.LastAlarm);                   // no sound possible, handled honestly
    }

    [Fact]
    public void Stop_while_degraded_silences_the_alarm()
    {
        var (engine, factory, _, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        factory.LastMic!.Fault(new InvalidOperationException("mic died"));
        engine.DrainPending();
        var alarm = factory.LastAlarm!;

        engine.Stop();
        engine.DrainPending();

        Assert.Equal(EngineState.Stopped, engine.State);
        Assert.Equal(DeviceState.Stopped, alarm.State);
    }

    // Mirrors AudioEngine.StallThresholdMs; kept here so the watchdog test reads clearly.
    private const int StallMs = 500;
}
