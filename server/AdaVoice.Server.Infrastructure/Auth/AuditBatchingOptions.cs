namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Tuning for <c>AuditFlushService</c>'s periodic batch persistence, bound from the
/// <c>Audit</c> configuration section. Defaults favour a small, security-relevant write load —
/// widen <see cref="MaxBatchSize"/>/<see cref="QueueCapacity"/> only if measured volume needs it.</summary>
public sealed class AuditBatchingOptions
{
    /// <summary>How often the background service drains the queue and saves to the database.</summary>
    public int FlushIntervalSeconds { get; init; } = 10;

    /// <summary>Upper bound on rows written per flush tick, so one huge burst does not turn
    /// into one huge <c>SaveChangesAsync</c>. Any remainder is picked up on the next tick.</summary>
    public int MaxBatchSize { get; init; } = 500;

    /// <summary>Bounded channel capacity. Once full, <c>WriteAsync</c> callers await enqueue
    /// (backpressure) rather than an entry being dropped — see <see cref="IAuditWriter"/>.</summary>
    public int QueueCapacity { get; init; } = 10_000;
}
