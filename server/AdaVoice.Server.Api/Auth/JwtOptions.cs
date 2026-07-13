namespace AdaVoice.Server.Api.Auth;

/// <summary>Non-secret JWT settings bound from configuration (<c>Jwt</c> section in
/// appsettings.json). <see cref="Kid"/> is intentionally empty in appsettings — it is supplied
/// at runtime from the <c>ADAVOICE_JWT_KID</c> environment variable, read directly by
/// <see cref="JwtKeyProvider"/> rather than trusted from config.</summary>
public sealed class JwtOptions
{
    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    public string Kid { get; init; } = string.Empty;

    public int AccessTokenMinutes { get; init; } = 15;
}
