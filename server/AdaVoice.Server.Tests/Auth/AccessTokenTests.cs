using System.Security.Cryptography;
using AdaVoice.Server.Api.Auth;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AdaVoice.Server.Tests.Auth;

public class AccessTokenTests
{
    private static (IAccessTokenIssuer issuer, ECDsaSecurityKey key) NewIssuer()
    {
        var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new ECDsaSecurityKey(ec) { KeyId = "test-kid" };
        var opts = new JwtOptions { Issuer = "adavoice-auth", Audience = "adavoice-api", Kid = "test-kid", AccessTokenMinutes = 15 };
        return (new AccessTokenIssuer(new StubKeyProvider(key), opts), key);
    }

    [Fact]
    public async Task Issued_token_is_es256_with_kid_and_expected_claims_and_15min_lifetime()
    {
        var (issuer, key) = NewIssuer();
        var userId = Guid.NewGuid(); var tenantId = Guid.NewGuid();

        var (token, expiresAt) = issuer.Issue(userId, tenantId, "super_admin");

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "adavoice-auth", ValidAudience = "adavoice-api",
            IssuerSigningKey = key, ValidAlgorithms = ["ES256"],
            ClockSkew = TimeSpan.FromMinutes(1),
        });
        Assert.True(result.IsValid);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal("ES256", jwt.Alg);
        Assert.Equal("test-kid", jwt.Kid);
        Assert.Equal(userId.ToString(), jwt.GetClaim("sub").Value);
        Assert.Equal(tenantId.ToString(), jwt.GetClaim("tenant_id").Value);
        Assert.Equal("super_admin", jwt.GetClaim("role").Value);
        Assert.False(string.IsNullOrEmpty(jwt.GetClaim("jti").Value));
        // 15-minute lifetime (allow a few seconds of issue-time slack).
        Assert.InRange((expiresAt - jwt.ValidFrom).TotalMinutes, 14.5, 15.5);
    }

    [Fact]
    public async Task Validation_rejects_a_non_es256_algorithm()
    {
        var (issuer, key) = NewIssuer();
        var (token, _) = issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), "operator");
        // Forge a header that claims alg=none by swapping the first segment.
        var parts = token.Split('.');
        var noneHeader = Base64UrlEncoder.Encode("""{"alg":"none","kid":"test-kid","typ":"JWT"}""");
        var forged = $"{noneHeader}.{parts[1]}.";
        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(forged, new TokenValidationParameters
        {
            ValidIssuer = "adavoice-auth", ValidAudience = "adavoice-api",
            IssuerSigningKey = key, ValidAlgorithms = ["ES256"], ClockSkew = TimeSpan.FromMinutes(1),
        });
        Assert.False(result.IsValid);
    }
}

file sealed class StubKeyProvider : IJwtKeyProvider
{
    public StubKeyProvider(ECDsaSecurityKey key)
    {
        SigningKey = key;
        PublicKey = key;
    }

    public ECDsaSecurityKey SigningKey { get; }

    public ECDsaSecurityKey PublicKey { get; }
}
