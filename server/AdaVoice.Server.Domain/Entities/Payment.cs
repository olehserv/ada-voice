using AdaVoice.Server.Domain.Abstractions;
using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>Settles an invoice. See docs/monetize/database-design.md §2 "payments".</summary>
public class Payment : IHasTimestamps
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public PaymentProvider Provider { get; set; }

    /// <summary>Unique per provider; null for manual payments.</summary>
    public string? ProviderTxId { get; set; }

    /// <summary>Actual received amount.</summary>
    public decimal AmountUah { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary>Who clicked "mark paid" (manual only).</summary>
    public Guid? MarkedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
