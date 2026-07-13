namespace AdaVoice.Server.Domain.Enums;

/// <summary>Lifecycle status of a tenant (customer company). Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum TenantStatus
{
    Active,
    Suspended,
    Cancelled,
    Deleted,
}
