using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace AdaVoice.Server.Api.Auth;

/// <summary>Issues ES256 access tokens: 15-minute lifetime (configurable), <c>kid</c> header, and
/// claims <c>sub</c> (user id), <c>tenant_id</c>, <c>role</c>, <c>jti</c>.</summary>
public sealed class AccessTokenIssuer : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly IJwtKeyProvider _keyProvider;
    private readonly JwtOptions _options;

    public AccessTokenIssuer(IJwtKeyProvider keyProvider, JwtOptions options)
    {
        _keyProvider = keyProvider;
        _options = options;
    }

    public (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, Guid tenantId, string roleText)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(_keyProvider.SigningKey, SecurityAlgorithms.EcdsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = userId.ToString(),
                ["tenant_id"] = tenantId.ToString(),
                ["role"] = roleText,
                ["jti"] = Guid.NewGuid().ToString(),
            },
        };

        var token = Handler.CreateToken(descriptor);
        return (token, new DateTimeOffset(expires, TimeSpan.Zero));
    }
}
