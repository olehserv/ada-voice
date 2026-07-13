namespace AdaVoice.Server.Domain.Abstractions;

/// <summary>Marks a tenant-owned entity with a required (non-null) tenant id. The
/// interceptor stamps <see cref="TenantId"/> from the ambient tenant when it is unset,
/// so callers never supply it — the single place tenant_id is assigned (§14 pitfall #16).
/// audit_logs deliberately does NOT implement this: its tenant is nullable (system rows).</summary>
public interface IHasTenant
{
    Guid TenantId { get; set; }
}
