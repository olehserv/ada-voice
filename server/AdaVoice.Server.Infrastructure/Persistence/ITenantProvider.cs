namespace AdaVoice.Server.Infrastructure.Persistence;

/// <summary>Supplies the ambient tenant for the current unit of work. The DbContext's
/// global query filters and the <see cref="AuditableTenantInterceptor"/> both read this
/// one source, so tenant scoping is decided in a single place (§14 pitfall #16).</summary>
public interface ITenantProvider
{
    /// <summary>The current tenant, or null when there is no tenant context (system work,
    /// design-time). A null tenant makes tenant-filtered queries return no rows.</summary>
    Guid? CurrentTenantId { get; }
}

/// <summary>A mutable <see cref="ITenantProvider"/>. Used by tests and by the design-time
/// factory (with a null tenant). Request-scoped DI wiring comes in a later phase.</summary>
public sealed class AmbientTenantProvider : ITenantProvider
{
    public Guid? CurrentTenantId { get; set; }
}
