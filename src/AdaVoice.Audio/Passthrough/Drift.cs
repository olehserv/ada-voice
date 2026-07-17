namespace AdaVoice.Audio.Passthrough;

/// <summary>Which kind of clock-drift event happened on the mic path (design 06 §1).</summary>
public enum DriftKind
{
    /// <summary>The mic ran ahead of the output; the backlog was dropped to stay low-latency.</summary>
    Overrun,

    /// <summary>The output ran ahead of the mic; silence was inserted to fill the gap.</summary>
    Underrun,
}

/// <summary>
/// Raised when the mic path drops audio (overrun) or inserts silence (underrun). The AudioEngine
/// subscribes and logs these, so a recurring cadence is visible and not silently shipped around
/// (design 06 §1).
/// </summary>
/// <remarks>
/// An overrun is raised on the capture thread; an underrun is raised on the render (read)
/// thread. Handlers must be fast and must not block — do the heavy work (logging) elsewhere,
/// for example by enqueueing the event.
/// </remarks>
public sealed class DriftEventArgs(DriftKind kind) : EventArgs
{
    public DriftKind Kind { get; } = kind;
}
