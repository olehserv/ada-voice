using System.Diagnostics;

namespace AdaVoice.Audio.Engine;

/// <summary>
/// The production <see cref="IEngineClock"/>: monotonic time from a <see cref="Stopwatch"/> and
/// periodic ticks from a <see cref="Timer"/>. The watchdog and the Degraded rebuild schedule run
/// off this in the real host. Pure .NET (no Windows), so it lives in the core and is unit-tested.
/// </summary>
public sealed class SystemEngineClock : IEngineClock
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public long NowMs => _stopwatch.ElapsedMilliseconds;

    public IDisposable SchedulePeriodic(int intervalMs, Action callback)
    {
        // The callback only enqueues a command (the engine wires it that way), but a timer callback
        // exception would be unobserved and could take down the process — swallow defensively so the
        // clock can never crash the host.
        return new Timer(_ =>
        {
            try { callback(); }
            catch { /* a tick is best-effort; never let it crash the process */ }
        }, state: null, dueTime: intervalMs, period: intervalMs);
    }
}
