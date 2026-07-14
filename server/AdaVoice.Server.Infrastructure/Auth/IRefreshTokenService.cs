namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>A freshly issued refresh token: the raw opaque value is returned to the caller
/// exactly once (only its SHA-256 hash is stored), alongside the token's expiry.</summary>
public readonly record struct IssuedRefreshToken(string RawToken, DateTimeOffset ExpiresAt);

/// <summary>Issues and (from Task 4) rotates opaque refresh tokens. The server stores only the
/// SHA-256 hash; the raw token never touches the database.</summary>
public interface IRefreshTokenService
{
    /// <summary>Issues the first token of a brand-new rotation family for a login.</summary>
    Task<IssuedRefreshToken> IssueNewFamilyAsync(Guid userId, DateTimeOffset now, CancellationToken ct);
}
