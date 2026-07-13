namespace AdaVoice.Server.Domain.Enums;

/// <summary>Lifecycle status of a tenant's subscription. Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum SubscriptionStatus
{
    Trial,
    Active,
    PastDue,
    GracePeriod,
    Suspended,
    Cancelled,
    Expired,
}
