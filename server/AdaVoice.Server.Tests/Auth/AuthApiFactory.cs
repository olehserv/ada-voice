using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace AdaVoice.Server.Tests.Auth;

/// <summary>In-process host for the auth integration tests, pointed at a test's throwaway
/// PostgreSQL database (via <see cref="PostgresFixture"/>) and signed with a shared ephemeral
/// ES256 key.
///
/// The signing key is a single process-wide key exported to the env vars the real
/// <c>JwtKeyProvider</c> reads. Those env vars are process-global, so every factory sets the
/// SAME value — parallel test classes cannot race to a different key. It is set in the STATIC
/// constructor because <c>JwtKeyProvider</c> is constructed eagerly at host-build time, before
/// <see cref="ConfigureWebHost"/> runs.</summary>
public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    public const string Kid = "test-kid";

    private static readonly ECDsa SharedSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    static AuthApiFactory()
    {
        Environment.SetEnvironmentVariable("ADAVOICE_JWT_SIGNING_KEY", SharedSigningKey.ExportPkcs8PrivateKeyPem());
        Environment.SetEnvironmentVariable("ADAVOICE_JWT_KID", Kid);
    }

    private readonly string _connectionString;
    private readonly int _authPermitPerMinute;

    public AuthApiFactory(string connectionString, int authPermitPerMinute = 1000)
    {
        _connectionString = connectionString;
        _authPermitPerMinute = authPermitPerMinute;
    }

    /// <summary>Every formatted log message the host emitted, for asserting no secret is logged.</summary>
    public ConcurrentQueue<string> LogMessages { get; } = new();

    /// <summary>The public half of the shared signing key, for validating issued access tokens.</summary>
    public ECDsaSecurityKey PublicSigningKey => new(SharedSigningKey) { KeyId = Kid };

    /// <summary>Mints an access token with an explicit expiry, signed with the shared key, so a
    /// test can present a token that is already expired (AC3) without waiting.</summary>
    public string CreateAccessToken(Guid userId, Guid tenantId, string role, DateTime expiresUtc)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "adavoice-auth",
            Audience = "adavoice-api",
            IssuedAt = expiresUtc.AddMinutes(-15),
            NotBefore = expiresUtc.AddMinutes(-15),
            Expires = expiresUtc,
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(SharedSigningKey) { KeyId = Kid }, SecurityAlgorithms.EcdsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = userId.ToString(),
                ["tenant_id"] = tenantId.ToString(),
                ["role"] = role,
                ["jti"] = Guid.NewGuid().ToString(),
            },
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Per-host (not process-global) settings, so parallel classes each use their own DB.
        // Cap this host's Npgsql pool so several parallel test hosts together stay well under
        // PostgreSQL's max_connections (100) — an uncapped pool per host exhausts it.
        var connection = new NpgsqlConnectionStringBuilder(_connectionString)
        {
            MaxPoolSize = 15,
        }.ConnectionString;

        builder.UseSetting("ADAVOICE_DB_CONNECTION", connection);
        builder.UseSetting("RateLimit:AuthPermitPerMinute", _authPermitPerMinute.ToString());
        // Audit rows are now persisted by a background flush (AuditFlushService), not
        // synchronously with the request. A 1s interval keeps integration tests fast without
        // relying on host-shutdown flush timing, which WebApplicationFactory disposal does not
        // guarantee (BackgroundService.StopAsync only runs if the host is gracefully stopped).
        builder.UseSetting("Audit:FlushIntervalSeconds", "1");

        builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(LogMessages)));
    }
}
