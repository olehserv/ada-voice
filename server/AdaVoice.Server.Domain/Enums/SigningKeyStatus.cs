namespace AdaVoice.Server.Domain.Enums;

/// <summary>Status of a JWS signing key. Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum SigningKeyStatus
{
    Active,
    Next,
    Retired,
}
