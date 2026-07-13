using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdaVoice.Server.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> build the context at design time (Task 3 migrations)
/// without a running Api host. Reads the connection string from <c>ADAVOICE_DB_CONNECTION</c>
/// and otherwise uses the documented docker-compose dev string. That fallback is a
/// throwaway local credential, not a secret. A null-tenant provider is passed because
/// migrations do not run tenant-scoped queries.</summary>
public sealed class AdaVoiceDbContextFactory : IDesignTimeDbContextFactory<AdaVoiceDbContext>
{
    // Dev-only docker-compose credentials (docs/monetize plan Global Constraints). Never a
    // production secret; production supplies ADAVOICE_DB_CONNECTION.
    private const string DevFallbackConnection =
        "Host=localhost;Port=5432;Database=adavoice;Username=adavoice;Password=adavoice_dev";

    public AdaVoiceDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ADAVOICE_DB_CONNECTION")
                         ?? DevFallbackConnection;

        var options = new DbContextOptionsBuilder<AdaVoiceDbContext>()
            .UseNpgsql(connection, o => o.MigrationsAssembly(typeof(AdaVoiceDbContext).Assembly.GetName().Name))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AdaVoiceDbContext(options, new AmbientTenantProvider { CurrentTenantId = null });
    }
}
