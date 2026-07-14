using System.Security.Claims;
using AdaVoice.Server.Api.Infrastructure;
using AdaVoice.Server.Domain.Entities;
using AdaVoice.Server.Domain.Enums;
using AdaVoice.Server.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;

namespace AdaVoice.Server.Api.Auth;

/// <summary>Maps the <c>/api/auth</c> endpoints. Endpoints stay thin: they orchestrate the
/// Infrastructure services (user lookup, lockout, refresh tokens, audit) and the Api's access-
/// token issuer. Login (Task 3) is here; refresh/logout/change-password/me arrive in Tasks 4-5.</summary>
public static class AuthEndpoints
{
    // A fixed dummy hash to verify against when no user matches, so unknown-email and
    // wrong-password logins take the same code path and similar time (§14 #4). Derived from the
    // injected hasher (cached once) so it always matches the configured work factor — a fresh
    // PasswordHasher<User> could diverge if PasswordHasherOptions are ever customised.
    private static string? _dummyPasswordHash;

    private static string DummyPasswordHash(IPasswordHasher<User> hasher) =>
        _dummyPasswordHash ??= hasher.HashPassword(new User(), "unused-dummy-password");

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").RequireRateLimiting(AuthRateLimit.PolicyName);
        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous();
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapGet("/me", MeAsync).RequireAuthorization();
        group.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext http,
        IUserAuthenticationService users,
        IRefreshTokenService refreshTokens,
        IAuditWriter audit,
        IAccessTokenIssuer accessTokens,
        IPasswordHasher<User> hasher,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ip = http.Connection.RemoteIpAddress?.ToString();

        var user = await users.FindActiveUserByEmailAsync(request.Email, ct);

        // Always verify a password (a dummy hash when there is no user) so the unknown-email and
        // wrong-password paths cost the same and reveal nothing (§14 #4).
        var verification = hasher.VerifyHashedPassword(
            user ?? new User(), user?.PasswordHash ?? DummyPasswordHash(hasher), request.Password);
        var passwordOk = verification != PasswordVerificationResult.Failed;

        if (user is null || !passwordOk)
        {
            if (user is not null)
            {
                var justLocked = await users.RegisterFailedAttemptAsync(user.Id, now, ct);
                if (justLocked)
                {
                    await audit.WriteAsync(
                        "auth.account_locked", "user", user.Id, user.TenantId, user.Id,
                        ActorType.User, ip, null, ct);
                }
            }

            await audit.WriteAsync(
                "auth.login_failed", "user", user?.Id, user?.TenantId, user?.Id,
                ActorType.User, ip, null, ct);
            return AuthProblems.InvalidCredentials(correlation);
        }

        // Correct password, but a locked account must still fail — and fail identically to a
        // wrong password, never revealing the lockout (SEC-03).
        if (users.IsLocked(user, now))
        {
            await audit.WriteAsync(
                "auth.login_failed", "user", user.Id, user.TenantId, user.Id,
                ActorType.User, ip, null, ct);
            return AuthProblems.InvalidCredentials(correlation);
        }

        await users.RegisterSuccessAsync(user.Id, now, ct);

        var (accessToken, accessTokenExpiresAt) =
            accessTokens.Issue(user.Id, user.TenantId, RoleClaimValue.For(user.Role));
        var refresh = await refreshTokens.IssueNewFamilyAsync(user.Id, now, ct);

        await audit.WriteAsync(
            "auth.login_succeeded", "user", user.Id, user.TenantId, user.Id,
            ActorType.User, ip, null, ct);

        return Results.Ok(new TokenResponse(
            accessToken, accessTokenExpiresAt, refresh.RawToken, refresh.ExpiresAt));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        HttpContext http,
        IRefreshTokenService refreshTokens,
        IUserAuthenticationService users,
        IAccessTokenIssuer accessTokens,
        IAuditWriter audit,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ip = http.Connection.RemoteIpAddress?.ToString();

        var outcome = await refreshTokens.RotateAsync(request.RefreshToken, now, ct);

        switch (outcome.Status)
        {
            case RotationStatus.Rotated:
                var user = await users.FindActiveUserByIdAsync(outcome.UserId, ct);
                if (user is null)
                {
                    // The token was valid but its user is gone/disabled — treat as invalid.
                    return AuthProblems.InvalidRefreshToken(correlation);
                }

                var (accessToken, accessTokenExpiresAt) =
                    accessTokens.Issue(user.Id, user.TenantId, RoleClaimValue.For(user.Role));
                await audit.WriteAsync(
                    "auth.token_refreshed", "user", user.Id, user.TenantId, user.Id,
                    ActorType.User, ip, null, ct);
                return Results.Ok(new TokenResponse(
                    accessToken, accessTokenExpiresAt, outcome.NewRawToken!, outcome.NewExpiresAt));

            case RotationStatus.Reuse:
                // Token-theft tripwire: the family was revoked. Audit and fail generically.
                await audit.WriteAsync(
                    "auth.refresh_reuse_detected", "user", outcome.UserId, null, outcome.UserId,
                    ActorType.User, ip, null, ct);
                return AuthProblems.InvalidRefreshToken(correlation);

            default: // NotFound, Expired
                return AuthProblems.InvalidRefreshToken(correlation);
        }
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest request,
        HttpContext http,
        ClaimsPrincipal principal,
        IRefreshTokenService refreshTokens,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ip = http.Connection.RemoteIpAddress?.ToString();

        await refreshTokens.RevokeFamilyByRawAsync(request.RefreshToken, now, ct);

        var userId = ParseGuidClaim(principal, "sub");
        var tenantId = ParseGuidClaim(principal, "tenant_id");
        await audit.WriteAsync(
            "auth.logout", "user", userId, tenantId, userId, ActorType.User, ip, null, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(
        ClaimsPrincipal principal,
        IUserAuthenticationService users,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var userId = ParseGuidClaim(principal, "sub");
        if (userId is null)
        {
            return AuthProblems.Unauthorized(correlation);
        }

        var user = await users.FindByIdAsync(userId.Value, ct);
        if (user is null)
        {
            // Token is valid but the user is gone/other-tenant — no longer a valid principal.
            return AuthProblems.Unauthorized(correlation);
        }

        return Results.Ok(new MeResponse(
            user.Id, user.Email, RoleClaimValue.For(user.Role), user.TenantId, user.DisplayName));
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        HttpContext http,
        ClaimsPrincipal principal,
        IUserAuthenticationService users,
        IRefreshTokenService refreshTokens,
        IPasswordHasher<User> hasher,
        IAuditWriter audit,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ip = http.Connection.RemoteIpAddress?.ToString();

        var userId = ParseGuidClaim(principal, "sub");
        if (userId is null)
        {
            return AuthProblems.Unauthorized(correlation);
        }

        var user = await users.FindByIdAsync(userId.Value, ct);
        if (user is null)
        {
            return AuthProblems.Unauthorized(correlation);
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verification == PasswordVerificationResult.Failed)
        {
            return AuthProblems.InvalidCredentials(correlation);
        }

        var newHash = hasher.HashPassword(user, request.NewPassword);
        await users.SetPasswordHashAsync(user.Id, newHash, now, ct);
        // Force re-login everywhere: every existing refresh token for this user is revoked.
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, ct);
        await audit.WriteAsync(
            "auth.password_changed", "user", user.Id, user.TenantId, user.Id, ActorType.User, ip, null, ct);

        return Results.NoContent();
    }

    private static Guid? ParseGuidClaim(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirst(claimType)?.Value, out var value) ? value : null;
}
