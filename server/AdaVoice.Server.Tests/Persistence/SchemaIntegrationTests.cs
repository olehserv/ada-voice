using Npgsql;

namespace AdaVoice.Server.Tests.Persistence;

// AC1: the migration creates all 13 canonical tables with snake_case names and timestamptz
// audit columns. Real PostgreSQL, so tagged Integration and skipped on the DB-less runner.
[Trait("Category", "Integration")]
public sealed class SchemaIntegrationTests : IClassFixture<PostgresFixture>
{
    private static readonly string[] ExpectedTables =
    {
        "tenants", "users", "plans", "subscriptions", "device_activations",
        "license_tickets", "invoices", "payments", "usage_events", "audit_logs",
        "refresh_tokens", "signing_keys", "idempotency_keys",
    };

    private readonly PostgresFixture _fixture;

    public SchemaIntegrationTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task All_thirteen_canonical_tables_exist()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        var actual = new HashSet<string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = 'public' AND table_type = 'BASE TABLE'",
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actual.Add(reader.GetString(0));
        }

        foreach (var table in ExpectedTables)
        {
            Assert.Contains(table, actual);
        }
    }

    [Fact]
    public async Task Audit_columns_are_timestamptz_and_append_only_tables_have_no_updated_at()
    {
        await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
        await connection.OpenAsync();

        // A representative table with both audit columns.
        Assert.Equal("timestamp with time zone", await ColumnTypeAsync(connection, "tenants", "created_at"));
        Assert.Equal("timestamp with time zone", await ColumnTypeAsync(connection, "tenants", "updated_at"));

        // Append-only tables keep created_at but deliberately omit updated_at.
        Assert.Equal("timestamp with time zone", await ColumnTypeAsync(connection, "audit_logs", "created_at"));
        Assert.Null(await ColumnTypeAsync(connection, "audit_logs", "updated_at"));

        Assert.Equal("timestamp with time zone", await ColumnTypeAsync(connection, "idempotency_keys", "created_at"));
        Assert.Null(await ColumnTypeAsync(connection, "idempotency_keys", "updated_at"));
    }

    // Returns the information_schema data_type for a column, or null when the column is absent.
    private static async Task<string?> ColumnTypeAsync(NpgsqlConnection connection, string table, string column)
    {
        await using var command = new NpgsqlCommand(
            "SELECT data_type FROM information_schema.columns " +
            "WHERE table_schema = 'public' AND table_name = @table AND column_name = @column",
            connection);
        command.Parameters.AddWithValue("table", table);
        command.Parameters.AddWithValue("column", column);
        return (string?)await command.ExecuteScalarAsync();
    }
}
