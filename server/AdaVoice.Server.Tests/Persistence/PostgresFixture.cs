using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AdaVoice.Server.Tests.Persistence;

/// <summary>Real-PostgreSQL fixture for the integration tests. Used as
/// <c>IClassFixture&lt;PostgresFixture&gt;</c> so EACH integration test class gets its OWN
/// throwaway database: create a uniquely-named DB on the target server, run the migration
/// once, and drop it on dispose. This isolates classes from each other and leaves the shared
/// <c>adavoice</c> dev database untouched.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Dev-only docker-compose credentials (plan Global Constraints). Not a production secret;
    // CI/prod override with ADAVOICE_DB_CONNECTION. The base string points at the maintenance
    // database (adavoice), which we connect to only to CREATE/DROP the throwaway test DB.
    private const string DevFallbackConnection =
        "Host=localhost;Port=5432;Database=adavoice;Username=adavoice;Password=adavoice_dev";

    private readonly List<AdaVoiceDbContext> _handedOut = new();
    private string _maintenanceConnectionString = string.Empty;
    private string _testDatabaseName = string.Empty;

    /// <summary>Connection string bound to this fixture's throwaway test database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var baseConnection = Environment.GetEnvironmentVariable("ADAVOICE_DB_CONNECTION")
                             ?? DevFallbackConnection;

        _maintenanceConnectionString = baseConnection;

        // Random (v4) GUID for a collision-free suffix. NOT a v7 GUID here: v7's leading
        // bytes are a millisecond timestamp, so two fixtures initializing in parallel in the
        // same millisecond would collide on the first 12 hex chars (duplicate database name).
        _testDatabaseName = "adavoice_test_" + Guid.NewGuid().ToString("N")[..12];

        ConnectionString = new NpgsqlConnectionStringBuilder(baseConnection)
        {
            Database = _testDatabaseName,
        }.ConnectionString;

        // CREATE DATABASE cannot run inside a transaction block, so use a plain command.
        await using (var admin = new NpgsqlConnection(_maintenanceConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{_testDatabaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        // Apply the InitialCreate migration once to the fresh database.
        await using var migrateContext = BuildContext(new AmbientTenantProvider { CurrentTenantId = null });
        await migrateContext.Database.MigrateAsync();
    }

    /// <summary>Returns a FRESH context bound to this fixture's test database with the given
    /// tenant provider. The context wires the audit/tenant interceptor itself (from the
    /// provider) in its OnConfiguring, so callers never register it here.</summary>
    public AdaVoiceDbContext CreateContext(ITenantProvider provider)
    {
        var context = BuildContext(provider);
        _handedOut.Add(context);
        return context;
    }

    public async Task DisposeAsync()
    {
        // Dispose every context we handed out (and the migrate context) so no managed
        // connection is left open, then clear Npgsql's physical pool for the test DB.
        foreach (var context in _handedOut)
        {
            await context.DisposeAsync();
        }

        // Clear ONLY this fixture's test-DB pool (not ClearAllPools) so disposing one class
        // does not disrupt a concurrently-running integration class's live connections.
        await using (var poolProbe = new NpgsqlConnection(ConnectionString))
        {
            NpgsqlConnection.ClearPool(poolProbe);
        }

        // WITH (FORCE) terminates any remaining backends (PostgreSQL 16). DROP DATABASE also
        // cannot run inside a transaction block, so use a plain command.
        await using var admin = new NpgsqlConnection(_maintenanceConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand(
            $"DROP DATABASE IF EXISTS \"{_testDatabaseName}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private AdaVoiceDbContext BuildContext(ITenantProvider provider)
    {
        var options = new DbContextOptionsBuilder<AdaVoiceDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AdaVoiceDbContext(options, provider);
    }
}
