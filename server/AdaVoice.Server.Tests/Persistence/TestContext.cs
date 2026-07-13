using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AdaVoice.Server.Tests.Persistence;

/// <summary>Builds an <see cref="AdaVoiceDbContext"/> for DB-less model-shape tests.
/// The connection string is a dummy: EF only needs it to pick the Npgsql provider and
/// build the model. No connection is ever opened by these tests.</summary>
internal static class TestContext
{
    // Any syntactically valid string works — the model builds without a live server.
    private const string DummyConnection = "Host=localhost;Database=x;Username=x;Password=x";

    public static AdaVoiceDbContext Create(Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<AdaVoiceDbContext>()
            .UseNpgsql(DummyConnection)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AdaVoiceDbContext(options, new AmbientTenantProvider { CurrentTenantId = tenantId });
    }
}
