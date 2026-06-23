namespace AdaVoice.Audio.Engine;

/// <summary>
/// Monotonic time plus periodic scheduling. Hidden behind a seam so tests can control time
/// (watchdog timeouts) and fire ticks by hand instead of sleeping.
/// </summary>
public interface IEngineClock
{
    /// <summary>A monotonically increasing millisecond counter (not wall-clock time).</summary>
    long NowMs { get; }

    /// <summary>Call <paramref name="callback"/> every <paramref name="intervalMs"/>. Dispose to stop.</summary>
    IDisposable SchedulePeriodic(int intervalMs, Action callback);
}
