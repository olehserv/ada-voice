using AdaVoice.Server.Domain.Entities;

namespace AdaVoice.Server.Infrastructure.Auth;

/// <summary>User lookup and lockout bookkeeping for the login flow. Login is anonymous (no
/// tenant claim), so the lookup and counter updates deliberately bypass the tenant query
/// filter — see the implementation's <c>tenant-scan-ok</c> markers.</summary>
public interface IUserAuthenticationService
{
    /// <summary>Finds the single active user with this email across all tenants, or null when
    /// there is no match OR more than one (ambiguous → treated as a failed login, no
    /// enumeration). Email matches case-insensitively (citext).</summary>
    Task<User?> FindActiveUserByEmailAsync(string email, CancellationToken ct);

    /// <summary>Finds the active user with this id, or null. Used by the anonymous refresh
    /// endpoint (which has a user id from the rotated token but no tenant claim) to read the
    /// tenant and role for the new access token — so it also bypasses the tenant filter.</summary>
    Task<User?> FindActiveUserByIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Atomically increments the failed-login counter and, if the threshold is
    /// crossed and the account is not already locked, sets <c>locked_until</c>. Returns true
    /// only when this call freshly locked the account (so the caller can audit it once).</summary>
    Task<bool> RegisterFailedAttemptAsync(Guid userId, DateTimeOffset now, CancellationToken ct);

    /// <summary>Resets the failed-login counter and lockout and stamps <c>last_login_at</c>
    /// on a successful login.</summary>
    Task RegisterSuccessAsync(Guid userId, DateTimeOffset now, CancellationToken ct);

    /// <summary>True when the user is currently within a lockout window.</summary>
    bool IsLocked(User user, DateTimeOffset now);

    /// <summary>Loads the user by id within the CURRENT tenant (respects the query filter) —
    /// for authenticated endpoints (/me, change-password) where the JWT supplies the tenant, so
    /// a caller can only ever reach their own tenant's row.</summary>
    Task<User?> FindByIdAsync(Guid userId, CancellationToken ct);

    /// <summary>Sets a new password hash for the user (within the current tenant).</summary>
    Task SetPasswordHashAsync(Guid userId, string passwordHash, DateTimeOffset now, CancellationToken ct);
}
