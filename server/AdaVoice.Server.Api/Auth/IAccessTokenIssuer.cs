namespace AdaVoice.Server.Api.Auth;

/// <summary>Issues short-lived ES256 access tokens. Consumed by the login and refresh endpoints
/// (Phase 2 Tasks 3-4).</summary>
public interface IAccessTokenIssuer
{
    /// <summary>Issues a signed access token for the given user/tenant/role.</summary>
    /// <returns>The compact JWT and the instant it expires at.</returns>
    (string Token, DateTimeOffset ExpiresAt) Issue(Guid userId, Guid tenantId, string roleText);
}
