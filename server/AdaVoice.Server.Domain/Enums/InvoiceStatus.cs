namespace AdaVoice.Server.Domain.Enums;

/// <summary>Lifecycle status of an invoice. Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum InvoiceStatus
{
    Draft,
    Issued,
    Paid,
    Overdue,
    Cancelled,
    Refunded,
}
