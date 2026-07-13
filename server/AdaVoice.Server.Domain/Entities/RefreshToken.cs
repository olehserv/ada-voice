namespace AdaVoice.Server.Domain.Entities;

/// <summary>Supports refresh-token rotation for a user's login session.
/// See docs/monetize/database-design.md §2 "refresh_tokens".</summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Null for admin-panel logins.</summary>
    public Guid? DeviceActivationId { get; set; }

    /// <summary>Unique; SHA-256 of the opaque token. Raw token never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Groups a rotation chain; reuse revokes the family.</summary>
    public Guid FamilyId { get; set; }

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Rotation link to the token that replaced this one.</summary>
    public Guid? ReplacedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
