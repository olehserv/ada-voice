namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Enqueues append-only audit rows for the background <c>AuditFlushService</c> to
/// batch-persist. There is deliberately no update/delete path.
///
/// Correlation id and event timestamp are captured HERE, at enqueue time, not left for the
/// flush: the flush runs later, in a scope with no HttpContext (so no ambient correlation id)
/// and, per <c>AuditableTenantInterceptor</c>, "now" at flush time would misdate every batched
/// row by up to the flush interval.
///
/// The enqueue deliberately uses <see cref="CancellationToken.None"/>, not the caller's
/// <paramref name="ct"/> equivalent: the write is an in-memory channel operation (near-instant),
/// and a client disconnecting right after triggering a security-relevant action (a failed
/// login, an account lockout) must not cancel — and therefore drop — that audit row.</summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly IAuditQueue _queue;
    private readonly ICorrelationContext _correlation;

    public AuditWriter(IAuditQueue queue, ICorrelationContext correlation)
    {
        _queue = queue;
        _correlation = correlation;
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken ct)
    {
        var queued = new QueuedAuditEntry(entry, _correlation.CorrelationId, DateTimeOffset.UtcNow);
        await _queue.EnqueueAsync(queued, CancellationToken.None);
    }
}
