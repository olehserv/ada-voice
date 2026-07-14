using AdaVoice.Server.Tests.Persistence;

namespace AdaVoice.Server.Tests.Auth;

/// <summary>Runs the auth integration classes SEQUENTIALLY against one shared PostgreSQL
/// database. Serializing them (instead of the default per-class parallelism) keeps the number
/// of live WebApplicationFactory hosts — and therefore open Npgsql connections — safely under
/// PostgreSQL's max_connections. Tests use unique random emails/tenants, so sharing one database
/// is safe.</summary>
[CollectionDefinition(Name)]
public sealed class ServerIntegrationCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "ServerIntegration";
}
