using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Appends one row to <c>audit_logs</c>. The correlation id is read from the ambient
/// <see cref="ICorrelationContext"/>; the client IP is passed in by the caller (the endpoint
/// owns the HTTP context, keeping Infrastructure free of ASP.NET types). <c>tenantId</c> is set
/// explicitly because <c>AuditLog</c> is not tenant-owned, so the save interceptor never stamps
/// it.</summary>
public interface IAuditWriter
{
    Task WriteAsync(
        string action,
        string entityType,
        Guid? entityId,
        Guid? tenantId,
        Guid? actorUserId,
        ActorType actorType,
        string? ip,
        string? dataJson,
        CancellationToken ct);
}
