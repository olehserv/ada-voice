namespace AdaVoice.Server.Domain.Enums;

/// <summary>Payment provider that settled a payment. Stored as text; see
/// docs/monetize/database-design.md §4 for the canonical text values.</summary>
public enum PaymentProvider
{
    ManualBankTransfer,
    LiqPay,
    WayForPay,
    Fondy,
}
