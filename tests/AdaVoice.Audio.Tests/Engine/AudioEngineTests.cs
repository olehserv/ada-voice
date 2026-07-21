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
    public void Failed_start_stays_stopped_and_surfaces_the_error()
    {
        var (engine, factory, _, events) = NewEngine();
        factory.FailNext(DeviceRole.Cable, transient: true, message: "no cable");

        engine.Start();
        engine.DrainPending(); // must not throw out of Handle

        Assert.Equal(EngineState.Stopped, engine.State);
        Assert.Contains(events, e => e is EngineEvent.StateChanged { State: EngineState.Stopped, Error: not null });

        // The partial graph must have been cleaned, so a good Start now reaches Live.
        engine.Start();
        engine.DrainPending();
        Assert.Equal(EngineState.Live, engine.State);
    }

    // A multi-capsule USB mic is real hardware, not corrupt input — Start must report the specific
    // reason (with the channel count) rather than falling through to the generic DeviceFailure text.
    [Fact]
    public void Start_with_a_multi_channel_mic_reports_the_channel_count()
    {
        var (engine, factory, _, events) = NewEngine();
        factory.MicFormat = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 4);

        engine.Start();
        engine.DrainPending();

        Assert.Equal(EngineState.Stopped, engine.State);
        Assert.Contains(events, e => e is EngineEvent.StateChanged
        {
            State: EngineState.Stopped,
            Error.Reason: EngineErrorReason.TooManyMicChannels,
            Error.Channels: 4,
        });
    }

    [Fact]
    public void Post_after_dispose_does_not_throw()
    {
        var (engine, _, _, _) = NewEngine();
        engine.Dispose();

        // A clock timer or device-monitor callback can post during shutdown after the queue is gone.
        engine.Post(new EngineCommand.WatchdogTick());
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

        // Drift is POSTED from the audio thread (never raised inline — the host logs to a file
        // on this event), so the DriftLogged re-raise happens when the control thread drains.
        Assert.DoesNotContain(events, e => e is EngineEvent.DriftLogged);
        engine.DrainPending();

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
        var oldCable = factory.LastCable!;
        oldCable.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        Assert.Equal(EngineState.Degraded, engine.State);

        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Live, engine.State);
        Assert.Equal(2, factory.CableCreateCount);               // a fresh cable was built
        Assert.Equal(1, oldCable.DisposeCount);                  // and the dead one was released
        Assert.Equal(DeviceState.Stopped, factory.LastAlarm!.State); // alarm silenced
        Assert.Contains(events, e => e is EngineEvent.RebuildResult { Role: DeviceRole.Cable, Success: true });
    }

    [Fact]
    public void Degraded_rebuilds_the_mic_and_audio_flows_again()
    {
        var (engine, factory, clock, events) = NewEngine();
        engine.Start();
        engine.DrainPending();

        var oldMic = factory.LastMic!;
        oldMic.Fault(new InvalidOperationException("mic died"));
        engine.DrainPending();
        Assert.Equal(EngineState.Degraded, engine.State);

        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Live, engine.State);
        Assert.Equal(1, oldMic.DisposeCount); // the dead capture was released, exactly once
        Assert.Contains(events, e => e is EngineEvent.RebuildResult { Role: DeviceRole.Mic, Success: true });

        // The cardinal assertion: the rebuilt mic must be re-wired into the mixer. Push to the NEW
        // mic and pull the cable — audio must flow, proving AddMixerInput ran, not just a state flip.
        factory.LastMic!.Push(TestAudio.Sine(440, 4800));
        factory.LastCable!.Pull(4800);
        Assert.Contains(factory.LastCable!.Captured, s => s != 0f);
    }

    // C1 regression: the player captures its duck target once, but RebuildMic replaces the
    // passthrough. Without the relay, ducking lands on the disposed old passthrough and the live
    // mic plays at full volume under every phrase for the rest of the session.
    [Fact]
    public void Mic_rebuild_keeps_the_playing_phrase_ducking_the_new_mic()
    {
        // Duck to silence with no ramp, so any mic leak-through is a hard failure.
        var factory = new FakeDeviceFactory();
        var clock = new ManualEngineClock();
        using var engine = new AudioEngine(factory, clock, new PhrasePlayerOptions { DuckGain = 0f, DuckRampMs = 0 });
        var events = new List<EngineEvent>();
        engine.Events += (_, e) => events.Add(e);
        engine.Start();
        engine.DrainPending();

        engine.Play(new Phrase("p", Enumerable.Repeat(0.25f, 48_000).ToArray()));
        engine.DrainPending();

        factory.LastMic!.Fault(new InvalidOperationException("mic died"));
        engine.DrainPending();
        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();
        Assert.Equal(EngineState.Live, engine.State);

        // Push voice into the REBUILT mic and pull the cable while the phrase still plays:
        // only the phrase (constant 0.25) may come out — the new mic must be fully ducked.
        factory.LastMic!.Push(TestAudio.Sine(440, 4800));
        factory.LastCable!.Pull(4800);
        Assert.NotEmpty(factory.LastCable!.Captured);
        Assert.All(factory.LastCable!.Captured, s => Assert.Equal(0.25f, s, precision: 3));
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

    // M4: OFF AIR must end the phrase. Left alone it keeps consuming inaudibly and its tail
    // would play into the call when the operator returns on air.
    [Fact]
    public void Entering_off_air_stops_the_active_phrase()
    {
        var (engine, factory, _, events) = NewEngine();
        engine.Start();
        engine.DrainPending();
        engine.Play(new Phrase("p", Enumerable.Repeat(0.5f, 48_000).ToArray()));
        engine.DrainPending();

        engine.EnterOffAir();
        engine.DrainPending();

        // The gate keeps pulling while OFF AIR, so the stop-fade drains and the phrase ends —
        // the UI glow clears via PhraseChanged(null).
        factory.LastCable!.Pull(48_000);
        Assert.Contains(events, e => e is EngineEvent.PhraseChanged { PhraseId: null });

        // Back on air: nothing of the phrase may remain — only silence (no mic pushed).
        engine.ExitOffAir();
        engine.DrainPending();
        var alreadyCaptured = factory.LastCable!.Captured.Count;
        factory.LastCable!.Pull(4800);
        Assert.All(factory.LastCable!.Captured.Skip(alreadyCaptured), s => Assert.Equal(0f, s));
    }

    // M5: OFF AIR requests made while Degraded must be honored on recovery — the operator who
    // pressed "back on air" during the outage must not silently return muted.
    [Fact]
    public void Exit_off_air_requested_while_degraded_is_honored_on_recovery()
    {
        var (engine, factory, clock, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        engine.EnterOffAir();
        engine.DrainPending();

        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        Assert.Equal(EngineState.Degraded, engine.State);

        engine.ExitOffAir(); // e.g. StopRecording while degraded
        engine.DrainPending();

        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Live, engine.State); // not silently back to OFF AIR
        factory.LastMic!.Push(TestAudio.Sine(440, 4800));
        factory.LastCable!.Pull(4800);
        Assert.Contains(factory.LastCable!.Captured, s => s != 0f); // and the gate is open
    }

    [Fact]
    public void Enter_off_air_requested_while_degraded_is_honored_on_recovery()
    {
        var (engine, factory, clock, _) = NewEngine();
        engine.Start();
        engine.DrainPending();

        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();

        engine.EnterOffAir(); // operator starts a recording flow during the outage
        engine.DrainPending();

        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.OffAir, engine.State);
    }

    // M6: the gate survives a cable rebuild with a pre-fault read stamp (necessarily older
    // than the stall threshold). Without a reset, the next watchdog tick would immediately
    // re-degrade — alarm blip, attempt-counter reset, flapping on slow drivers.
    [Fact]
    public void Recovered_cable_is_not_re_degraded_by_the_stale_stall_stamp()
    {
        var (engine, factory, clock, _) = NewEngine();
        engine.Start();
        engine.DrainPending();
        factory.LastCable!.Pull(480); // one real read stamps the gate at t=0
        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();

        clock.Advance(StallMs + FirstBackoffMs + 100); // well past both backoff and threshold
        clock.FireTicks();
        engine.DrainPending();
        Assert.Equal(EngineState.Live, engine.State); // rebuilt

        // The next tick fires before the new render thread's first pull.
        clock.Advance(100);
        clock.FireTicks();
        engine.DrainPending();
        Assert.Equal(EngineState.Live, engine.State); // no flap
    }

    // M7: the alarm is retried on the backoff schedule while Degraded — a failed first start
    // (default output busy/gone at fault time) must not mean a silent DEGRADED forever.
    [Fact]
    public void Failed_alarm_is_retried_on_the_rebuild_schedule()
    {
        var (engine, factory, clock, _) = NewEngine();
        engine.Start();
        engine.DrainPending();

        factory.FailNext(DeviceRole.Alarm, transient: true); // first StartAlarm fails
        factory.FailNext(DeviceRole.Cable, transient: true); // and the first rebuild fails too
        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        Assert.Null(factory.LastAlarm); // silent DEGRADED right now

        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Degraded, engine.State); // rebuild failed as armed…
        Assert.NotNull(factory.LastAlarm);                // …but the alarm sounds now
        Assert.Equal(DeviceState.Running, factory.LastAlarm!.State);
    }

    [Fact]
    public void Faulted_alarm_is_rebuilt_on_the_next_attempt()
    {
        var (engine, factory, clock, _) = NewEngine();
        engine.Start();
        engine.DrainPending();

        factory.FailNext(DeviceRole.Cable, transient: true); // keep the spell Degraded
        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        var firstAlarm = factory.LastAlarm!;
        Assert.Equal(DeviceState.Running, firstAlarm.State);

        firstAlarm.Fault(new InvalidOperationException("alarm device unplugged"));
        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.NotSame(firstAlarm, factory.LastAlarm); // a fresh alarm was built
        Assert.Equal(DeviceState.Running, factory.LastAlarm!.State);
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

    // H1 regression: seam construction and Init/Start run outside the factory's guarded region,
    // so a rebuild can throw something that is NOT an AudioDeviceException (e.g. a replugged
    // cable at the wrong rate → NotSupportedException from Init). That must behave like a
    // transient failure with backoff — before the fix it escaped AttemptRebuild, the schedule
    // never advanced, and the 100 ms watchdog became a tight device-churn loop.
    [Fact]
    public void Non_device_exception_during_rebuild_backs_off_instead_of_churning()
    {
        var (engine, factory, clock, events) = NewEngine();
        engine.Start();
        engine.DrainPending();
        factory.LastCable!.Fault(new InvalidOperationException("cable died"));
        engine.DrainPending();
        Assert.Equal(1, factory.CableCreateCount);

        // The cable comes back at 44.1 kHz: Init throws NotSupportedException.
        factory.CableFormat = WaveFormat.CreateIeeeFloatWaveFormat(44_100, 1);
        clock.Advance(FirstBackoffMs);
        clock.FireTicks();
        engine.DrainPending();

        Assert.Equal(EngineState.Degraded, engine.State);
        Assert.Contains(events, e => e is EngineEvent.RebuildResult { Success: false });
        Assert.Equal(2, factory.CableCreateCount);

        // Ticks inside the next backoff window must NOT attempt again — no 10 Hz churn.
        clock.Advance(100);
        clock.FireTicks();
        engine.DrainPending();
        Assert.Equal(2, factory.CableCreateCount);

        // After the longer backoff the cable is back at the right rate → normal recovery.
        factory.CableFormat = null;
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

    [Fact]
    public void SetDuckLevel_takes_effect_and_survives_a_stop_start_rebuild()
    {
        var (engine, factory, _, _) = NewEngine();
        engine.Start();
        engine.DrainPending();

        // Full mute when ducking, so the cable shows only the (ducked) mic under a silent phrase.
        engine.SetDuckLevel(0f, rampMs: 10);
        engine.DrainPending();

        AssertMicFullyDuckedUnderSilentPhrase(engine, factory);

        // Stop + Start rebuilds the graph (a new PhrasePlayer from the original options). The chosen
        // duck level must be re-applied, not reset to the -12 dB default.
        engine.Stop();
        engine.DrainPending();
        engine.Start();
        engine.DrainPending();

        AssertMicFullyDuckedUnderSilentPhrase(engine, factory);
    }

    // Plays a silent phrase (so the only cable audio is the mic), pushes a mic tone, and asserts the
    // steady-state tail is silent — i.e. the mic was ducked to zero. The ramp settles well before the tail.
    private static void AssertMicFullyDuckedUnderSilentPhrase(AudioEngine engine, FakeDeviceFactory factory)
    {
        engine.Play(new Phrase("silent", new float[48_000]));
        engine.DrainPending();

        factory.LastMic!.Push(TestAudio.Sine(440, 4800));
        factory.LastCable!.Pull(4800);

        var tail = factory.LastCable!.Captured.Skip(2400);
        Assert.All(tail, s => Assert.True(Math.Abs(s) < 1e-4f, $"expected ducked silence, got {s}"));

        engine.StopPhrase();
        engine.DrainPending();
    }

    // Mirrors AudioEngine.StallThresholdMs; kept here so the watchdog test reads clearly.
    private const int StallMs = 500;

    // Mirrors the first AudioEngine backoff step.
    private const int FirstBackoffMs = 250;
}
