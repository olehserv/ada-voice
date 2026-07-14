using System.Security.Cryptography;
using System.Text;
using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Infrastructure.Persistence;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>Issues opaque 256-bit refresh tokens. Only the SHA-256 hash is persisted
/// (<c>token_hash</c>); the raw value is returned to the caller once and never stored.
/// Rotation and reuse detection are added in Task 4.</summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly AdaVoiceDbContext _db;
    private readonly AuthPolicyOptions _policy;

    public RefreshTokenService(AdaVoiceDbContext db, AuthPolicyOptions policy)
    {
        _db = db;
        _policy = policy;
    }

    public async Task<IssuedRefreshToken> IssueNewFamilyAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var raw = GenerateRawToken();
        var token = new RefreshToken
        {
            UserId = userId,
            DeviceActivationId = null, // not device-bound in Phase 2 (no device_activations yet).
            TokenHash = Hash(raw),
            FamilyId = Guid.CreateVersion7(),
            IssuedAt = now,
            ExpiresAt = now.AddDays(_policy.RefreshSlidingDays),
        };

        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync(ct);

        return new IssuedRefreshToken(raw, token.ExpiresAt);
    }

    /// <summary>32 cryptographically-random bytes, base64url-encoded (256 bits of entropy).</summary>
    internal static string GenerateRawToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>SHA-256 of the raw token, uppercase hex. Deterministic so the hot
    /// <c>token_hash</c> lookup on refresh is a plain equality match.</summary>
    internal static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
