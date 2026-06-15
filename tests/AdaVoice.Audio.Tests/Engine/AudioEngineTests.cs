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
}
