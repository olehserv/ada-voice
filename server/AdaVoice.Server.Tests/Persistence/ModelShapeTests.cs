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
}
