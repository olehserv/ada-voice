namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>A freshly issued refresh token: the raw opaque value is returned to the caller
/// exactly once (only its SHA-256 hash is stored), alongside the token's expiry.</summary>
public readonly record struct IssuedRefreshToken(string RawToken, DateTimeOffset ExpiresAt);

/// <summary>Result of a rotation attempt.</summary>
public enum RotationStatus
{
    /// <summary>No token matched the presented hash.</summary>
    NotFound,

    /// <summary>The token was already rotated or revoked — a reuse tripwire; the whole family
    /// has been revoked (§14 #3 / token-theft detection).</summary>
    Reuse,

    /// <summary>The token was valid but past its sliding or absolute lifetime.</summary>
    Expired,

    /// <summary>The token was rotated; a replacement was issued.</summary>
    Rotated,
}

/// <summary>Outcome of <see cref="IRefreshTokenService.RotateAsync"/>. <see cref="NewRawToken"/>
/// and <see cref="NewExpiresAt"/> are set only when <see cref="Status"/> is
/// <see cref="RotationStatus.Rotated"/>.</summary>
public readonly record struct RotationOutcome(
    RotationStatus Status,
    Guid UserId,
    Guid FamilyId,
    string? NewRawToken,
    DateTimeOffset NewExpiresAt);

/// <summary>Issues, rotates, and revokes opaque refresh tokens. The server stores only the
/// SHA-256 hash; the raw token never touches the database.</summary>
public interface IRefreshTokenService
{
    /// <summary>Issues the first token of a brand-new rotation family for a login.</summary>
    Task<IssuedRefreshToken> IssueNewFamilyAsync(Guid userId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Rotates a presented refresh token in one transaction under a row lock, so two
    /// concurrent uses of the same token cannot both succeed (§14 #3). Presenting an
    /// already-rotated/revoked token revokes the entire family (reuse tripwire).</summary>
    Task<RotationOutcome> RotateAsync(string rawToken, DateTimeOffset now, CancellationToken ct);

    /// <summary>Revokes the whole family the presented token belongs to (logout). No-op if the
    /// token is unknown.</summary>
    Task RevokeFamilyByRawAsync(string rawToken, DateTimeOffset now, CancellationToken ct);
}
