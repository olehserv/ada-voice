using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Playback;
using AdaVoice.Audio.Tests.Engine.Fakes;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave;

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
    public void Alarm_sounds_even_when_the_default_output_is_not_48k()
    {
        // The alarm device is the system default output, often 44.1 kHz. The tone must be built at
        // the device's rate or the real render seam throws on Init and the alarm never sounds.
        var factory = new FakeDeviceFactory { AlarmFormat = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 1) };
        var clock = new ManualEngineClock();
        using var engine = new AudioEngine(factory, clock);
        engine.Start();
        engine.DrainPending();

        factory.LastMic!.Fault(new InvalidOperationException("mic died"));
        engine.DrainPending();

        Assert.Equal(EngineState.Degraded, engine.State);
        Assert.NotNull(factory.LastAlarm);
        Assert.Equal(DeviceState.Running, factory.LastAlarm!.State);
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

    [Fact]
    public void Degraded_rebuilds_the_cable_and_returns_to_live()
    {
        var (engine, factory, clock, events) = NewEngine();
        engine.Start();
        engine.DrainPending();
        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        Assert.Equal(EngineState.Degraded, engine.State);

        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Live, engine.State);
        Assert.Equal(2, factory.CableCreateCount);               // a fresh cable was built
        Assert.Equal(DeviceState.Stopped, factory.LastAlarm!.State); // alarm silenced
        Assert.Contains(events, e => e is EngineEvent.RebuildResult { Role: DeviceRole.Cable, Success: true });
    }

    [Fact]
    public void Degraded_rebuilds_the_mic_and_audio_flows_again()
    {
        var (engine, factory, clock, events) = NewEngine();
        engine.Start();
        engine.DrainPending();

        factory.LastMic!.Fault(new InvalidOperationException("mic died"));
        engine.DrainPending();
        Assert.Equal(EngineState.Degraded, engine.State);

        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Live, engine.State);
        Assert.Contains(events, e => e is EngineEvent.RebuildResult { Role: DeviceRole.Mic, Success: true });

        // The cardinal assertion: the rebuilt mic must be re-wired into the mixer. Push to the NEW
        // mic and pull the cable — audio must flow, proving AddMixerInput ran, not just a state flip.
        factory.LastMic!.Push(TestAudio.Sine(440, 4800));
        factory.LastCable!.Pull(4800);
        Assert.Contains(factory.LastCable!.Captured, s => s != 0f);
    }

    [Fact]
    public void Rebuild_after_a_fault_in_off_air_returns_to_off_air()
    {
        var (engine, factory, clock, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        engine.EnterOffAir();
        engine.DrainPending();

        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.OffAir, engine.State);

        // The cable is still gated to silence after recovery.
        var cable = factory.LastCable!;
        factory.LastMic!.Push(TestAudio.Sine(440, 4800));
        cable.Pull(4800);
        Assert.All(cable.Captured, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Rebuild_waits_for_the_backoff_delay()
    {
        var (engine, factory, clock, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        Assert.Equal(1, factory.CableCreateCount);

        clock.Advance(FirstBackoffMs - 50); // not yet due
        clock.FireTicks();
        engine.DrainPending();
        Assert.Equal(EngineState.Degraded, engine.State);
        Assert.Equal(1, factory.CableCreateCount);

        clock.Advance(50); // now due
        clock.FireTicks();
        engine.DrainPending();
        Assert.Equal(EngineState.Live, engine.State);
        Assert.Equal(2, factory.CableCreateCount);
    }

    [Fact]
    public void Transient_rebuild_failure_keeps_retrying_with_backoff()
    {
        var (engine, factory, clock, events) = NewEngine();
        engine.Start();
        engine.DrainPending();
        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();

        // First attempt (after 250 ms) fails transiently.
        factory.FailNext(DeviceRole.Cable, transient: true);
        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();
        Assert.Equal(EngineState.Degraded, engine.State);
        Assert.Contains(events, e => e is EngineEvent.RebuildResult { Success: false });

        // Next attempt is scheduled later (500 ms), not immediately.
        clock.Advance(200);
        clock.FireTicks();
        engine.DrainPending();
        Assert.Equal(EngineState.Degraded, engine.State);

        // After the longer backoff it succeeds.
        clock.Advance(400);
        clock.FireTicks();
        engine.DrainPending();
        Assert.Equal(EngineState.Live, engine.State);
    }

    [Fact]
    public void Terminal_rebuild_failure_stops_the_engine()
    {
        var (engine, factory, clock, events) = NewEngine();
        engine.Start();
        engine.DrainPending();
        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        var alarm = factory.LastAlarm!;

        factory.FailNext(DeviceRole.Cable, transient: false); // non-recoverable
        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Stopped, engine.State);
        Assert.Equal(DeviceState.Stopped, alarm.State); // alarm silenced on terminal stop
        Assert.Contains(events, e => e is EngineEvent.StateChanged { State: EngineState.Stopped, Error: not null });
    }

    [Fact]
    public void Device_removed_while_live_goes_degraded()
    {
        var (engine, factory, _, _) = NewEngine();
        engine.Start();
        engine.DrainPending();

        engine.Post(new EngineCommand.DeviceChanged(DeviceRole.Cable, DeviceChangeKind.Removed));
        engine.DrainPending();

        Assert.Equal(EngineState.Degraded, engine.State);
        Assert.NotNull(factory.LastAlarm);
    }

    [Fact]
    public void Device_arrived_rebuilds_immediately_skipping_backoff()
    {
        var (engine, factory, clock, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        Assert.Equal(1, factory.CableCreateCount);

        // Far short of the 250 ms first backoff, the replugged device triggers an immediate retry.
        clock.Advance(10);
        engine.Post(new EngineCommand.DeviceChanged(DeviceRole.Cable, DeviceChangeKind.Added));
        engine.DrainPending();
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Live, engine.State);
        Assert.Equal(2, factory.CableCreateCount);
    }

    // Mirrors AudioEngine.StallThresholdMs; kept here so the watchdog test reads clearly.
    private const int StallMs = 500;

    // Mirrors the first AudioEngine backoff step.
    private const int FirstBackoffMs = 250;
}
