using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

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

    /// <summary>The public half of the shared signing key, for validating issued access tokens.</summary>
    public ECDsaSecurityKey PublicSigningKey => new(SharedSigningKey) { KeyId = Kid };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Per-host (not process-global) settings, so parallel classes each use their own DB.
        builder.UseSetting("ADAVOICE_DB_CONNECTION", _connectionString);
        builder.UseSetting("RateLimit:AuthPermitPerMinute", _authPermitPerMinute.ToString());
    }
}
