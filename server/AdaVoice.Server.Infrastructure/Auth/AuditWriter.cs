using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Infrastructure.Persistence;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Writes append-only audit rows. There is deliberately no update/delete path.
/// <c>CreatedAt</c> is stamped by the save interceptor (AuditLog is <c>IHasCreatedAt</c>);
/// everything else is set here from the caller's arguments and the ambient correlation id.</summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly AdaVoiceDbContext _db;
    private readonly ICorrelationContext _correlation;

    public AuditWriter(AdaVoiceDbContext db, ICorrelationContext correlation)
    {
        _db = db;
        _correlation = correlation;
    }

    public async Task WriteAsync(
        string action,
        string entityType,
        Guid? entityId,
        Guid? tenantId,
        Guid? actorUserId,
        ActorType actorType,
        string? ip,
        string? dataJson,
        CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = tenantId,
            ActorUserId = actorUserId,
            ActorType = actorType,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Ip = ip,
            CorrelationId = _correlation.CorrelationId,
            Data = dataJson,
        });

        await _db.SaveChangesAsync(ct);
    }
}
