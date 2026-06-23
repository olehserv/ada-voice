using AdaVoice.Audio.Engine;

namespace AdaVoice.Audio.Tests.Engine;

public class SystemEngineClockTests
{
    [Fact]
    public void NowMs_advances_over_time()
    {
        var clock = new SystemEngineClock();
        var start = clock.NowMs;

        Assert.True(SpinWait.SpinUntil(() => clock.NowMs > start, TimeSpan.FromSeconds(2)),
            "monotonic time should advance");
    }

    [Fact]
    public void SchedulePeriodic_fires_until_disposed()
    {
        var clock = new SystemEngineClock();
        var count = 0;
        var handle = clock.SchedulePeriodic(10, () => Interlocked.Increment(ref count));

        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref count) > 0, TimeSpan.FromSeconds(2)),
            "the callback should fire at least once");

        handle.Dispose();
        Thread.Sleep(40);                       // let any in-flight callback finish
        var afterDispose = Volatile.Read(ref count);
        Thread.Sleep(80);                       // several more intervals would pass if still running

        Assert.Equal(afterDispose, Volatile.Read(ref count)); // no ticks after dispose
    }
}
