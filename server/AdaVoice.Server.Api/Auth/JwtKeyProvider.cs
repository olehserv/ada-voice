using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace AdaVoice.Server.Api.Auth;

/// <summary>Loads the ES256 signing key from the environment at startup. The private key never
/// lives in configuration or source — only in <c>ADAVOICE_JWT_SIGNING_KEY</c> (EC private key
/// PEM, PKCS#8 or SEC1) — so it fails fast at construction if the environment variable is
/// missing rather than silently falling back to a baked-in key.</summary>
public sealed class JwtKeyProvider : IJwtKeyProvider
{
    public JwtKeyProvider()
    {
        var pem = Environment.GetEnvironmentVariable("ADAVOICE_JWT_SIGNING_KEY")
            ?? throw new InvalidOperationException(
                "ADAVOICE_JWT_SIGNING_KEY is not set. The JWT signing key must come from the " +
                "environment; there is no baked-in fallback.");
        var kid = Environment.GetEnvironmentVariable("ADAVOICE_JWT_KID")
            ?? throw new InvalidOperationException("ADAVOICE_JWT_KID is not set.");

        var ec = ECDsa.Create();
        ec.ImportFromPem(pem);

        // ECDsa holds both the private and public halves, so the same key serves signing
        // (private) and validation (public).
        var key = new ECDsaSecurityKey(ec) { KeyId = kid };
        SigningKey = key;
        PublicKey = key;
    }

    public ECDsaSecurityKey SigningKey { get; }

    public ECDsaSecurityKey PublicKey { get; }
}
