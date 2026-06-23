using AdaVoice.Audio.Engine;

namespace AdaVoice.Audio.Tests.Engine.Fakes;

/// <summary>A clock the test fully controls. Time only moves when the test sets it, and
/// periodic callbacks fire only when the test calls <see cref="FireTicks"/>.</summary>
public sealed class ManualEngineClock : IEngineClock
{
    private readonly List<Action> _callbacks = [];

    public long NowMs { get; set; }

    public void Advance(long ms) => NowMs += ms;

    public IDisposable SchedulePeriodic(int intervalMs, Action callback)
    {
        _callbacks.Add(callback);
        return new Stop(() => _callbacks.Remove(callback));
    }

    /// <summary>Fire every scheduled periodic callback once (simulates one timer tick).</summary>
    public void FireTicks()
    {
        foreach (var cb in _callbacks.ToArray())
            cb();
    }

    private sealed class Stop(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
