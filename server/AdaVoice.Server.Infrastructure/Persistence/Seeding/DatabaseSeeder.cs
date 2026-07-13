using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AdaVoice.Server.Infrastructure.Persistence.Seeding;

/// <summary>Idempotent bootstrap data: the system tenant, the default plan(s), and (optionally)
/// the first super_admin account. Every insert is guarded by an existence check, so calling
/// <see cref="SeedAsync"/> any number of times never creates duplicates (AC3).
///
/// Runs against a context built with a NULL <see cref="ITenantProvider"/> (the system path): the
/// <see cref="AuditableTenantInterceptor"/> leaves an explicitly-assigned TenantId alone when no
/// ambient tenant is set, which is what lets this seeder place rows in the system tenant. That
/// same null provider means the global query filter on <c>users</c> — which compares
/// <c>TenantId == CurrentTenantId</c> — would hide EVERY row (a non-null Guid never equals a null
/// tenant), so the super_admin existence check below deliberately bypasses it.
/// No startup wiring: this phase only builds the seeder, it does not call it from Program.cs.</summary>
public sealed class DatabaseSeeder
{
    /// <summary>Fixed, deterministic id for the system tenant that houses super_admin users.
    /// A constant (not generated) so re-running the seeder recognizes the same row every time.</summary>
    public static readonly Guid SystemTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string SystemTenantName = "System";
    private const string SystemTenantContactEmail = "system@adavoice.local";

    // §2 "plans": the one default plan implied by the brief. Values are sensible MVP defaults,
    // not a pricing catalog — add more plans later if the product needs tiers.
    private const string StandardPlanCode = "standard";
    private const string StandardPlanName = "Standard";
    private const decimal StandardPlanPriceUah = 999m;
    private const int StandardPlanMaxDevices = 3;
    private const int StandardPlanMaxPhrases = 500;
    private const string StandardPlanFeatures = "[\"phrase_library\"]";
    private const int StandardPlanTrialGraceDays = 2;
    private const int StandardPlanPaidGraceDays = 7;

    private readonly AdaVoiceDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(AdaVoiceDbContext db, IPasswordHasher<User> hasher, ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _hasher = hasher;
        _logger = logger;
    }

    public async Task SeedAsync(SuperAdminSeedOptions? superAdmin, CancellationToken ct = default)
    {
        await SeedSystemTenantAsync(ct);
        await SeedDefaultPlansAsync(ct);

        if (superAdmin is null)
        {
            _logger.LogWarning("super_admin seed skipped: ADAVOICE_SEED_SUPERADMIN_* not set");
            return;
        }

        await SeedSuperAdminAsync(superAdmin, ct);
    }

    private async Task SeedSystemTenantAsync(CancellationToken ct)
    {
        var exists = await _db.Tenants.AnyAsync(t => t.Id == SystemTenantId, ct);
        if (exists)
        {
            _logger.LogInformation("System tenant already exists ({TenantId})", SystemTenantId);
            return;
        }

        _db.Tenants.Add(new Tenant
        {
            Id = SystemTenantId,
            Name = SystemTenantName,
            Status = TenantStatus.Active,
            ContactEmail = SystemTenantContactEmail,
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created system tenant ({TenantId})", SystemTenantId);
    }

    private async Task SeedDefaultPlansAsync(CancellationToken ct)
    {
        var exists = await _db.Plans.AnyAsync(p => p.Code == StandardPlanCode, ct);
        if (exists)
        {
            _logger.LogInformation("Plan already exists ({PlanCode})", StandardPlanCode);
            return;
        }

        _db.Plans.Add(new Plan
        {
            Id = Guid.CreateVersion7(),
            Code = StandardPlanCode,
            Name = StandardPlanName,
            PriceUah = StandardPlanPriceUah,
            MaxDevices = StandardPlanMaxDevices,
            MaxPhrases = StandardPlanMaxPhrases,
            Features = StandardPlanFeatures,
            TrialGraceDays = StandardPlanTrialGraceDays,
            PaidGraceDays = StandardPlanPaidGraceDays,
            IsActive = true,
        });

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Created plan ({PlanCode})", StandardPlanCode);
    }

    private async Task SeedSuperAdminAsync(SuperAdminSeedOptions superAdmin, CancellationToken ct)
    {
        // tenant-scan-ok: seeder runs under a null-tenant system provider, so the global query
        // filter (TenantId == CurrentTenantId) would otherwise hide every row here, not just
        // other tenants' — this existence check must see the system tenant's own users.
        var exists = await _db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == SystemTenantId && u.Email == superAdmin.Email, ct);

        if (exists)
        {
            // Outcome only — never the password (§14 #19).
            _logger.LogInformation("super_admin already exists ({Email})", superAdmin.Email);
            return;
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            TenantId = SystemTenantId,
            Email = superAdmin.Email,
            Role = UserRole.SuperAdmin,
            Status = UserStatus.Active,
            LastLoginAt = null, // null forces a password change on first login (Phase 2).
        };
        user.PasswordHash = _hasher.HashPassword(user, superAdmin.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // Outcome only — never the password (§14 #19).
        _logger.LogInformation("Created super_admin ({Email})", superAdmin.Email);
    }
}
