using AdaVoice.Server.Domain.Abstractions;
using AdaVoice.Server.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AdaVoice.Server.Tests.Persistence;

// DB-less tests: they only build the EF model (OnModelCreating) and inspect its shape.
// No PostgreSQL is contacted, so they carry NO [Trait("Category","Integration")] tag.
public class ModelShapeTests
{
    // EF Core 10 obsoletes IReadOnlyEntityType.GetQueryFilter() (single-filter API) in
    // favour of the named-filter collection GetDeclaredQueryFilters(). Under
    // TreatWarningsAsErrors the old call would fail the build, so we assert on the collection.
    [Fact]
    public void Tenant_owned_entities_have_a_global_query_filter()
    {
        using var ctx = TestContext.Create();
        foreach (var t in new[]
                 {
                     typeof(User), typeof(Subscription), typeof(DeviceActivation),
                     typeof(Invoice), typeof(UsageEvent),
                 })
        {
            Assert.NotEmpty(ctx.Model.FindEntityType(t)!.GetDeclaredQueryFilters());
        }
    }

    [Fact]
    public void AuditLog_is_deliberately_not_filtered()
    {
        using var ctx = TestContext.Create();
        Assert.Empty(ctx.Model.FindEntityType(typeof(AuditLog))!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void LicenseTicket_primary_key_is_jti()
    {
        using var ctx = TestContext.Create();
        var pk = ctx.Model.FindEntityType(typeof(LicenseTicket))!.FindPrimaryKey()!;
        Assert.Equal("Jti", Assert.Single(pk.Properties).Name);
    }

    [Fact]
    public void Model_builds_for_all_thirteen_entities()
    {
        using var ctx = TestContext.Create();
        var mapped = ctx.Model.GetEntityTypes().Count();
        Assert.Equal(13, mapped);
    }

    // Fix 1 proof: the no-navigation FK relationships were wired. DB-less: inspects the model only.
    [Fact]
    public void User_has_a_foreign_key_to_Tenant()
    {
        using var ctx = TestContext.Create();
        var fks = ctx.Model.FindEntityType(typeof(User))!.GetForeignKeys();
        Assert.Contains(fks, fk => fk.PrincipalEntityType.ClrType == typeof(Tenant));
    }

    [Fact]
    public void RefreshToken_has_a_self_referential_foreign_key()
    {
        using var ctx = TestContext.Create();
        var fks = ctx.Model.FindEntityType(typeof(RefreshToken))!.GetForeignKeys();
        Assert.Contains(fks, fk => fk.PrincipalEntityType.ClrType == typeof(RefreshToken));
    }

    // Couples the query-filter code in AdaVoiceDbContext.ApplyTenantQueryFilters to the
    // IHasTenant marker interface itself, rather than to a second hardcoded entity list.
    // A future IHasTenant entity added without a HasQueryFilter line — which would silently
    // leak reads across tenants — now fails this test instead of shipping with a green suite.
    // Equally, a filter applied to a non-IHasTenant entity fails it too: the assertion is the
    // exact biconditional (filter exists) <=> (entity implements IHasTenant).
    [Fact]
    public void Query_filter_exists_exactly_for_IHasTenant_entities()
    {
        using var ctx = TestContext.Create();

        foreach (var entityType in ctx.Model.GetEntityTypes())
        {
            var isTenantOwned = typeof(IHasTenant).IsAssignableFrom(entityType.ClrType);
            var hasFilter = entityType.GetDeclaredQueryFilters().Any();

            if (isTenantOwned)
            {
                Assert.True(
                    hasFilter,
                    $"{entityType.ClrType.Name} implements IHasTenant but has no query filter.");
            }
            else
            {
                Assert.False(
                    hasFilter,
                    $"{entityType.ClrType.Name} does not implement IHasTenant but has a query filter.");
            }
        }
    }
}
