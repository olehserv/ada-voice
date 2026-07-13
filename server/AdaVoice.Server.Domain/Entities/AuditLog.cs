using AdaVoice.Server.Domain.Abstractions;
using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>Append-only audit record; no UPDATE/DELETE from app code. No UpdatedAt —
/// rows never change. Implements only <see cref="IHasCreatedAt"/> (not IHasTenant): its
/// tenant is nullable for system rows. See docs/monetize/database-design.md §2 "audit_logs".</summary>
public class AuditLog : IHasCreatedAt
{
    public Guid Id { get; set; }

    /// <summary>Null for system-wide actions.</summary>
    public Guid? TenantId { get; set; }

    /// <summary>Null for system jobs and webhooks.</summary>
    public Guid? ActorUserId { get; set; }

    public ActorType ActorType { get; set; }

    /// <summary>e.g. invoice.mark_paid, subscription.suspended.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>e.g. invoice.</summary>
    public string EntityType { get; set; } = string.Empty;

    public Guid? EntityId { get; set; }
    public string? Ip { get; set; }
    public string? CorrelationId { get; set; }

    /// <summary>Raw JSON before/after snapshot or details.</summary>
    public string? Data { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
