using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <inheritdoc />
public sealed class UserAuthenticationService : IUserAuthenticationService
{
    private readonly AdaVoiceDbContext _db;
    private readonly AuthPolicyOptions _policy;

    public UserAuthenticationService(AdaVoiceDbContext db, AuthPolicyOptions policy)
    {
        _db = db;
        _policy = policy;
    }

    public async Task<User?> FindActiveUserByEmailAsync(string email, CancellationToken ct)
    {
        // tenant-scan-ok: login carries no tenant, so under the anonymous request's null-tenant
        // provider the users query filter (TenantId == null) would hide every row. Look the user
        // up globally by email; requiring a single active match keeps the response generic (no
        // enumeration) when zero or several tenants share an email.
        var matches = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Email == email && u.Status == UserStatus.Active)
            .Take(2)
            .ToListAsync(ct);

        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<User?> FindActiveUserByIdAsync(Guid userId, CancellationToken ct)
    {
        // tenant-scan-ok: the refresh endpoint is anonymous (no tenant claim), so the users
        // filter would hide the row; load by primary key across tenants to mint the new token.
        return await _db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == userId && u.Status == UserStatus.Active, ct);
    }

    public async Task<bool> RegisterFailedAttemptAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        // tenant-scan-ok: anonymous login path; the atomic counter update must reach the real
        // user row regardless of the (null) tenant filter. Doing the increment in the database
        // (not read-modify-write in C#) prevents parallel attempts from losing counts (§14 #5).
        await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.FailedLoginCount, u => u.FailedLoginCount + 1)
                    .SetProperty(u => u.UpdatedAt, now),
                ct);

        // Lock atomically once the threshold is crossed and the account is not already inside a
        // live lockout window (LockedUntil in the future); the LockedUntil < now clause lets a
        // lockout re-arm after it expires. The counter is reset to 0 as the lock is set, so §8's
        // "15 min after 10 failed logins" means a fresh 10-attempt budget per window rather than
        // a single failure re-locking a user who waited out the previous window.
        // tenant-scan-ok: same anonymous-login reason as the increment above.
        var lockedNow = await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId
                && u.FailedLoginCount >= _policy.MaxFailedLogins
                && (u.LockedUntil == null || u.LockedUntil < now))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.LockedUntil, now.AddMinutes(_policy.LockoutMinutes))
                    .SetProperty(u => u.FailedLoginCount, 0)
                    .SetProperty(u => u.UpdatedAt, now),
                ct);

        return lockedNow > 0;
    }

    public async Task RegisterSuccessAsync(Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        // tenant-scan-ok: anonymous login path; reset the real user row regardless of the tenant
        // filter (null provider would otherwise match no rows).
        await _db.Users
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(u => u.FailedLoginCount, 0)
                    .SetProperty(u => u.LockedUntil, (DateTimeOffset?)null)
                    .SetProperty(u => u.LastLoginAt, now)
                    .SetProperty(u => u.UpdatedAt, now),
                ct);
    }

    public bool IsLocked(User user, DateTimeOffset now) =>
        user.LockedUntil is { } lockedUntil && lockedUntil > now;
}
