using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdaVoice.Server.Infrastructure.Persistence;

/// <summary>The monetization backend's EF Core context. Owns the 13 canonical tables,
/// snake_case naming, UUID v7 keys, timestamptz audit columns, and the multi-tenant global
/// query filters. Tenant scope comes from the injected <see cref="ITenantProvider"/>; the
/// same provider drives the <see cref="AuditableTenantInterceptor"/>, so reads and writes
/// agree on one tenant source.</summary>
public sealed class AdaVoiceDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public AdaVoiceDbContext(DbContextOptions<AdaVoiceDbContext> options, ITenantProvider tenantProvider)
        : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<DeviceActivation> DeviceActivations => Set<DeviceActivation>();
    public DbSet<LicenseTicket> LicenseTickets => Set<LicenseTicket>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Wire the audit/tenant interceptor here from the injected provider, so every
        // context instance stamps timestamps and tenant_id from one shared source.
        // Callers only supply the provider; they never register the interceptor themselves.
        optionsBuilder.AddInterceptors(new AuditableTenantInterceptor(_tenantProvider));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdaVoiceDbContext).Assembly);
        ApplyTenantQueryFilters(modelBuilder);
    }

    // Global query filters live here (not in the per-entity configs) because they must
    // close over THIS context's _tenantProvider field. EF re-evaluates that member per
    // query, so two contexts with different providers filter to different tenants.
    // audit_logs is deliberately excluded: its tenant is nullable and super_admin needs
    // cross-tenant reads. Tables with no tenant_id are not filtered.
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasQueryFilter(e => e.TenantId == _tenantProvider.CurrentTenantId);
        modelBuilder.Entity<Subscription>().HasQueryFilter(e => e.TenantId == _tenantProvider.CurrentTenantId);
        modelBuilder.Entity<DeviceActivation>().HasQueryFilter(e => e.TenantId == _tenantProvider.CurrentTenantId);
        modelBuilder.Entity<Invoice>().HasQueryFilter(e => e.TenantId == _tenantProvider.CurrentTenantId);
        modelBuilder.Entity<UsageEvent>().HasQueryFilter(e => e.TenantId == _tenantProvider.CurrentTenantId);
    }
}
