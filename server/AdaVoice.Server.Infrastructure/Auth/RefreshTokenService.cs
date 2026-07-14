using System.Security.Cryptography;
using System.Text;
using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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

    public async Task<RotationOutcome> RotateAsync(string rawToken, DateTimeOffset now, CancellationToken ct)
    {
        var hash = Hash(rawToken);

        // One transaction with a row lock so two concurrent uses of the same token serialize and
        // cannot both rotate it (§14 #3): the second waits on the lock, then sees the row already
        // rotated and trips the reuse tripwire below.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // FromSql (interpolated) parameterizes {hash} — it is NOT FromSqlRaw, so no injection and
        // no query-filter bypass (refresh_tokens is not a tenant-owned entity). FOR UPDATE takes
        // the row lock. The returned entity is tracked, so the rotation writes below are stamped
        // by the save interceptor.
        var row = await _db.RefreshTokens
            .FromSql($"SELECT * FROM refresh_tokens WHERE token_hash = {hash} FOR UPDATE")
            .FirstOrDefaultAsync(ct);

        if (row is null)
        {
            await tx.RollbackAsync(ct);
            return new RotationOutcome(RotationStatus.NotFound, Guid.Empty, Guid.Empty, null, default);
        }

        // Reuse: an already-rotated (ReplacedById set) or revoked token was presented again.
        // Revoke the whole family — the token-theft tripwire.
        if (row.RevokedAt is not null || row.ReplacedById is not null)
        {
            await RevokeFamilyAsync(row.FamilyId, now, ct);
            await tx.CommitAsync(ct);
            return new RotationOutcome(RotationStatus.Reuse, row.UserId, row.FamilyId, null, default);
        }

        var familyIssuedAt = await _db.RefreshTokens
            .Where(r => r.FamilyId == row.FamilyId)
            .MinAsync(r => r.IssuedAt, ct);
        var absoluteCap = familyIssuedAt.AddDays(_policy.RefreshAbsoluteDays);

        if (row.ExpiresAt <= now || now > absoluteCap)
        {
            row.RevokedAt = now;
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new RotationOutcome(RotationStatus.Expired, row.UserId, row.FamilyId, null, default);
        }

        // Rotate: issue a replacement in the same family, capped by the absolute lifetime, then
        // mark this token revoked and link it to its replacement.
        var raw = GenerateRawToken();
        var slidingExpiry = now.AddDays(_policy.RefreshSlidingDays);
        var newExpiresAt = slidingExpiry < absoluteCap ? slidingExpiry : absoluteCap;

        var replacement = new RefreshToken
        {
            UserId = row.UserId,
            DeviceActivationId = row.DeviceActivationId,
            TokenHash = Hash(raw),
            FamilyId = row.FamilyId,
            IssuedAt = now,
            ExpiresAt = newExpiresAt,
        };
        _db.RefreshTokens.Add(replacement);
        await _db.SaveChangesAsync(ct); // assigns replacement.Id

        row.RevokedAt = now;
        row.ReplacedById = replacement.Id;
        await _db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return new RotationOutcome(RotationStatus.Rotated, row.UserId, row.FamilyId, raw, newExpiresAt);
    }

    public async Task RevokeFamilyByRawAsync(string rawToken, DateTimeOffset now, CancellationToken ct)
    {
        var hash = Hash(rawToken);
        var row = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);
        if (row is null)
        {
            return;
        }

        await RevokeFamilyAsync(row.FamilyId, now, ct);
    }

    private Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct) =>
        _db.RefreshTokens
            .Where(r => r.FamilyId == familyId && r.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.RevokedAt, now).SetProperty(r => r.UpdatedAt, now), ct);

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
