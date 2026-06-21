using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
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
}
