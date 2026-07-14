namespace AdaVoice.Server.Api.Auth;

/// <summary>Login request. <c>DeviceId</c> is accepted for forward-compatibility with device
/// binding (Phase 4) but is not used to bind the refresh token in Phase 2.</summary>
public sealed record LoginRequest(string Email, string Password, Guid? DeviceId);

/// <summary>The token pair returned by login and refresh. The access token is a short-lived
/// ES256 JWT; the refresh token is the raw opaque value (stored server-side only as a hash).</summary>
public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Body for refresh and logout: the opaque refresh token.</summary>
public sealed record RefreshRequest(string RefreshToken);

/// <summary>Body for change-password.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>The current user, from GET /api/auth/me.</summary>
public sealed record MeResponse(Guid UserId, string Email, string Role, Guid TenantId, string? DisplayName);
