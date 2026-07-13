namespace AdaVoice.Server.Domain.Enums;

/// <summary>Status of a device activation. Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum DeviceStatus
{
    Active,
    Revoked,
    Blocked,
    Expired,
}
