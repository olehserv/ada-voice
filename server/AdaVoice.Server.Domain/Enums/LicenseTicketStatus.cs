namespace AdaVoice.Server.Domain.Enums;

/// <summary>Status of an issued license ticket. Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum LicenseTicketStatus
{
    Issued,
    Revoked,
}
