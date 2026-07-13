using Microsoft.IdentityModel.Tokens;

namespace AdaVoice.Server.Api.Auth;

/// <summary>Supplies the ES256 signing/validation key pair for access tokens. Split out as an
/// interface so tests can substitute an ephemeral key instead of the real
/// <see cref="JwtKeyProvider"/> (which requires the signing-key environment variable).</summary>
public interface IJwtKeyProvider
{
    /// <summary>The private key used to sign new access tokens. <c>KeyId</c> is the configured
    /// <c>kid</c>.</summary>
    ECDsaSecurityKey SigningKey { get; }

    /// <summary>The key used by JwtBearer to validate incoming tokens. For ECDsa, the same
    /// <see cref="ECDsaSecurityKey"/> holds both halves, so this is typically the same instance
    /// as <see cref="SigningKey"/>.</summary>
    ECDsaSecurityKey PublicKey { get; }
}
