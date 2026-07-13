using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>A JWS signing key for license tickets. Standalone table, no FKs.
/// See docs/monetize/database-design.md §2 "signing_keys".</summary>
public class SigningKey
{
    public Guid Id { get; set; }

    /// <summary>Unique; goes into the JWS header.</summary>
    public string Kid { get; set; } = string.Empty;

    /// <summary>e.g. ES256.</summary>
    public string Algorithm { get; set; } = string.Empty;

    /// <summary>Served via JWKS.</summary>
    public string PublicKeyPem { get; set; } = string.Empty;

    /// <summary>Encrypted with the master key from an env var.</summary>
    public byte[] PrivateKeyEncrypted { get; set; } = [];

    public SigningKeyStatus Status { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
