using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>Bills a subscription period. See docs/monetize/database-design.md §2 "invoices".</summary>
public class Invoice
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid SubscriptionId { get; set; }

    /// <summary>Unique, e.g. AV-2026-0001.</summary>
    public string Number { get; set; } = string.Empty;

    public InvoiceStatus Status { get; set; }
    public decimal AmountUah { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>Path/key of the stored PDF.</summary>
    public string? PdfPath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
