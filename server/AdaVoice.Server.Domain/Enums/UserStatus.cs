namespace AdaVoice.Server.Domain.Enums;

/// <summary>Account status of a user. Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum UserStatus
{
    Active,
    Disabled,
}
