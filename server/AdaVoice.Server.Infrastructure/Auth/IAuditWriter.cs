namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Enqueues one row for <c>audit_logs</c>. The correlation id is read from the ambient
/// <see cref="ICorrelationContext"/>; the client IP is passed in by the caller (the endpoint
/// owns the HTTP context, keeping Infrastructure free of ASP.NET types). <c>TenantId</c> on
/// <see cref="AuditEntry"/> is set explicitly because <c>AuditLog</c> is not tenant-owned, so
/// the save interceptor never stamps it.
///
/// The row is not written synchronously: it is persisted by a background flush on a
/// configurable interval (batched for write efficiency — see
/// <c>AuditFlushService</c>/<c>AuditQueue</c>). <paramref name="ct"/> is accepted for interface
/// stability but deliberately unused by the enqueue: a client disconnecting right after
/// triggering a security-relevant action (a failed login, an account lockout) must not cancel —
/// and thereby drop — its own audit row, so the underlying enqueue always runs to completion.</summary>
public interface IAuditWriter
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct);
}
