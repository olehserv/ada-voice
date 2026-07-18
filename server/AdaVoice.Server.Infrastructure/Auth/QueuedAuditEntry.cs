namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>An <see cref="AuditEntry"/> plus the request-scoped facts that must be captured at
/// enqueue time, before the row moves onto a background service with no HTTP context and no
/// request-scoped services: the correlation id (ambient per request) and the real event
/// timestamp. <see cref="CreatedAt"/> is what <c>AuditableTenantInterceptor</c> honours instead
/// of stamping "now" at whatever moment the background flush happens to run.</summary>
public sealed record QueuedAuditEntry(AuditEntry Entry, string CorrelationId, DateTimeOffset CreatedAt);
