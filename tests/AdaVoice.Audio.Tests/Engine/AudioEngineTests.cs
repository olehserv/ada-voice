using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Tests.Engine.Fakes;

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
}
