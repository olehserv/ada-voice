using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AdaVoice.Server.Tests.Persistence;

// AC2 + §14 pitfall #16: global query filters scope reads to the current tenant, and the
// interceptor stamps tenant_id from the single shared provider on writes (never the caller).
// Real PostgreSQL, so tagged Integration. Each test mints fresh tenant ids so the two tests
// stay independent even though they share the class's one database.
[Trait("Category", "Integration")]
public sealed class TenantIsolationTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _fixture;

    public TenantIsolationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Query_returns_only_the_current_tenants_rows()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedTenantsAndOneUserEachAsync(tenantA, tenantB);

        // Fresh context, provider = A: sees only A's user. Use LINQ (not Find) so the
        // global query filter is actually exercised.
        await using (var a = _fixture.CreateContext(Provider(tenantA)))
        {
            var users = await a.Users.ToListAsync();
            var user = Assert.Single(users);
            Assert.Equal(tenantA, user.TenantId);
        }

        // A SEPARATE fresh context, provider = B: sees only B's user. Never mutate one provider.
        await using (var b = _fixture.CreateContext(Provider(tenantB)))
        {
            var users = await b.Users.ToListAsync();
            var user = Assert.Single(users);
            Assert.Equal(tenantB, user.TenantId);
        }

        // IgnoreQueryFilters bypasses tenant scoping and sees both of this test's users.
        await using (var unscoped = _fixture.CreateContext(Provider(tenantA)))
        {
            var both = await unscoped.Users
                .IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantA || u.TenantId == tenantB)
                .ToListAsync();
            Assert.Equal(2, both.Count);
        }
    }

    [Fact]
    public async Task Interceptor_stamps_tenant_id_from_the_provider_not_the_caller()
    {
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        await SeedTenantsAndOneUserEachAsync(tenantA, tenantB);

        // Add a user WITHOUT setting TenantId, under provider = A → stamped to A.
        var stampedId = Guid.CreateVersion7();
        await using (var a = _fixture.CreateContext(Provider(tenantA)))
        {
            var user = NewUser(stampedId, "stamped@example.com");
            a.Users.Add(user);
            await a.SaveChangesAsync();
            Assert.Equal(tenantA, user.TenantId);
        }

        // Add a user with TenantId explicitly set to B, under provider = A → interceptor
        // OVERRIDES it to A (tenant_id comes from the one shared place, not the caller).
        var overriddenId = Guid.CreateVersion7();
        await using (var a = _fixture.CreateContext(Provider(tenantA)))
        {
            var user = NewUser(overriddenId, "overridden@example.com");
            user.TenantId = tenantB;
            a.Users.Add(user);
            await a.SaveChangesAsync();
            Assert.Equal(tenantA, user.TenantId);
        }

        // Both rows belong to A: visible to a fresh A context.
        await using (var a = _fixture.CreateContext(Provider(tenantA)))
        {
            Assert.NotNull(await a.Users.SingleOrDefaultAsync(u => u.Id == stampedId));
            Assert.NotNull(await a.Users.SingleOrDefaultAsync(u => u.Id == overriddenId));
        }

        // Invisible to a fresh B context — proving neither row leaked to the caller-named tenant.
        await using (var b = _fixture.CreateContext(Provider(tenantB)))
        {
            Assert.Null(await b.Users.SingleOrDefaultAsync(u => u.Id == stampedId));
            Assert.Null(await b.Users.SingleOrDefaultAsync(u => u.Id == overriddenId));
        }
    }

    // Seeds two tenants and one user under each via a NULL-tenant (system) provider: the
    // interceptor leaves the explicitly-assigned TenantId as-is on that path.
    private async Task SeedTenantsAndOneUserEachAsync(Guid tenantA, Guid tenantB)
    {
        await using var seed = _fixture.CreateContext(Provider(null));
        seed.Tenants.Add(NewTenant(tenantA, "Tenant A"));
        seed.Tenants.Add(NewTenant(tenantB, "Tenant B"));

        var userA = NewUser(Guid.CreateVersion7(), $"a-{tenantA:N}@example.com");
        userA.TenantId = tenantA;
        var userB = NewUser(Guid.CreateVersion7(), $"b-{tenantB:N}@example.com");
        userB.TenantId = tenantB;
        seed.Users.Add(userA);
        seed.Users.Add(userB);

        await seed.SaveChangesAsync();
    }

    private static AmbientTenantProvider Provider(Guid? tenantId) => new() { CurrentTenantId = tenantId };

    private static Tenant NewTenant(Guid id, string name) => new()
    {
        Id = id,
        Name = name,
        Status = TenantStatus.Active,
        ContactEmail = "owner@example.com",
    };

    private static User NewUser(Guid id, string email) => new()
    {
        Id = id,
        Email = email,
        PasswordHash = "x",
        Role = UserRole.Operator,
        Status = UserStatus.Active,
    };
}
