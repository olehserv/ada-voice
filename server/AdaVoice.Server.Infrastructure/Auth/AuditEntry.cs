using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>The caller-supplied facts for one audit row. Required properties must be set
/// by name at the call site — this replaces a long positional parameter list where two
/// same-typed <c>Guid?</c> arguments (<c>entityId</c>/<c>actorUserId</c>) were easy to
/// transpose. Deliberately excludes <see cref="Guid"/> for the correlation id (read from
/// the ambient <see cref="ICorrelationContext"/> by the writer) and the row's timestamp
/// (see <see cref="IAuditWriter"/> for why that is captured at enqueue time, not here).</summary>
public sealed record AuditEntry
{
    public required string Action { get; init; }
    public required string EntityType { get; init; }
    public Guid? EntityId { get; init; }
    public Guid? TenantId { get; init; }
    public Guid? ActorUserId { get; init; }
    public required ActorType ActorType { get; init; }
    public string? Ip { get; init; }
    public string? DataJson { get; init; }
}
