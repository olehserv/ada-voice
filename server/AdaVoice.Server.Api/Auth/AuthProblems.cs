using AdaVoice.Server.Infrastructure.Auth;

namespace AdaVoice.Server.Api.Auth;

/// <summary>RFC 7807 problem responses for the auth endpoints, each carrying a stable
/// <c>code</c> and the request's <c>correlationId</c>. The login failure response is
/// deliberately identical for wrong password, unknown email, and locked account (SEC-03 /
/// §14 #4) — callers must not be able to tell them apart.</summary>
public static class AuthProblems
{
    private const string TypeBase = "https://adavoice.example/problems/";

    public static IResult InvalidCredentials(ICorrelationContext correlation) => Problem(
        StatusCodes.Status401Unauthorized,
        "invalid_credentials",
        "Authentication failed",
        "The email or password is incorrect.",
        correlation);

    public static IResult Unauthorized(ICorrelationContext correlation) => Problem(
        StatusCodes.Status401Unauthorized,
        "unauthorized",
        "Unauthorized",
        "A valid access token is required.",
        correlation);

    public static IResult InvalidRefreshToken(ICorrelationContext correlation) => Problem(
        StatusCodes.Status401Unauthorized,
        "invalid_refresh_token",
        "Invalid refresh token",
        "The refresh token is unknown, expired, or has already been used.",
        correlation);

    private static IResult Problem(
        int status, string code, string title, string detail, ICorrelationContext correlation) =>
        Results.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            type: TypeBase + code,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["correlationId"] = correlation.CorrelationId,
            });
}
