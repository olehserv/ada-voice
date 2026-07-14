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
        var group = app.MapGroup("/api/auth");
        group.MapPost("/login", LoginAsync).AllowAnonymous();
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
}
