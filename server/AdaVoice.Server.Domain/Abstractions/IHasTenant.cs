namespace AdaVoice.Server.Domain.Abstractions;

/// <summary>Marks a tenant-owned entity with a required (non-null) tenant id. When an
/// ambient tenant is present, the interceptor OVERRIDES any caller-supplied
/// <see cref="TenantId"/> with it — the single shared place tenant_id is assigned
/// (§14 pitfall #16). With no ambient tenant (the system/seed path), the explicitly
/// assigned value is kept.
/// audit_logs deliberately does NOT implement this: its tenant is nullable (system rows).</summary>
public interface IHasTenant
{
    Guid TenantId { get; set; }
}
