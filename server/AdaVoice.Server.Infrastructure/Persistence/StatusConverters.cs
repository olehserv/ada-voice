using AdaVoice.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AdaVoice.Server.Infrastructure.Persistence;

/// <summary>Explicit enum↔text value converters. Statuses are stored as PostgreSQL
/// <c>text</c> + CHECK (database-design.md §4), never as native PG enums. We map the exact
/// canonical text (e.g. <c>PastDue</c> → <c>past_due</c>) instead of relying on
/// <c>Enum.ToString()</c>, so a C# rename can never silently change the stored value.
/// Converters are stateless and shared as <c>static readonly</c> across configurations.</summary>
internal static class StatusConverters
{
    public static readonly ValueConverter<TenantStatus, string> TenantStatus = Build(
        (Domain.Enums.TenantStatus.Active, "active"),
        (Domain.Enums.TenantStatus.Suspended, "suspended"),
        (Domain.Enums.TenantStatus.Cancelled, "cancelled"),
        (Domain.Enums.TenantStatus.Deleted, "deleted"));

    public static readonly ValueConverter<UserRole, string> UserRole = Build(
        (Domain.Enums.UserRole.Operator, "operator"),
        (Domain.Enums.UserRole.TenantAdmin, "tenant_admin"),
        (Domain.Enums.UserRole.SuperAdmin, "super_admin"));

    public static readonly ValueConverter<UserStatus, string> UserStatus = Build(
        (Domain.Enums.UserStatus.Active, "active"),
        (Domain.Enums.UserStatus.Disabled, "disabled"));

    public static readonly ValueConverter<SubscriptionStatus, string> SubscriptionStatus = Build(
        (Domain.Enums.SubscriptionStatus.Trial, "trial"),
        (Domain.Enums.SubscriptionStatus.Active, "active"),
        (Domain.Enums.SubscriptionStatus.PastDue, "past_due"),
        (Domain.Enums.SubscriptionStatus.GracePeriod, "grace_period"),
        (Domain.Enums.SubscriptionStatus.Suspended, "suspended"),
        (Domain.Enums.SubscriptionStatus.Cancelled, "cancelled"),
        (Domain.Enums.SubscriptionStatus.Expired, "expired"));

    public static readonly ValueConverter<DeviceStatus, string> DeviceStatus = Build(
        (Domain.Enums.DeviceStatus.Active, "active"),
        (Domain.Enums.DeviceStatus.Revoked, "revoked"),
        (Domain.Enums.DeviceStatus.Blocked, "blocked"),
        (Domain.Enums.DeviceStatus.Expired, "expired"));

    public static readonly ValueConverter<InvoiceStatus, string> InvoiceStatus = Build(
        (Domain.Enums.InvoiceStatus.Draft, "draft"),
        (Domain.Enums.InvoiceStatus.Issued, "issued"),
        (Domain.Enums.InvoiceStatus.Paid, "paid"),
        (Domain.Enums.InvoiceStatus.Overdue, "overdue"),
        (Domain.Enums.InvoiceStatus.Cancelled, "cancelled"),
        (Domain.Enums.InvoiceStatus.Refunded, "refunded"));

    public static readonly ValueConverter<PaymentProvider, string> PaymentProvider = Build(
        (Domain.Enums.PaymentProvider.ManualBankTransfer, "manual_bank_transfer"),
        (Domain.Enums.PaymentProvider.LiqPay, "liqpay"),
        (Domain.Enums.PaymentProvider.WayForPay, "wayforpay"),
        (Domain.Enums.PaymentProvider.Fondy, "fondy"));

    public static readonly ValueConverter<LicenseTicketStatus, string> LicenseTicketStatus = Build(
        (Domain.Enums.LicenseTicketStatus.Issued, "issued"),
        (Domain.Enums.LicenseTicketStatus.Revoked, "revoked"));

    public static readonly ValueConverter<SigningKeyStatus, string> SigningKeyStatus = Build(
        (Domain.Enums.SigningKeyStatus.Active, "active"),
        (Domain.Enums.SigningKeyStatus.Next, "next"),
        (Domain.Enums.SigningKeyStatus.Retired, "retired"));

    public static readonly ValueConverter<ActorType, string> ActorType = Build(
        (Domain.Enums.ActorType.User, "user"),
        (Domain.Enums.ActorType.System, "system"),
        (Domain.Enums.ActorType.Admin, "admin"));

    private static ValueConverter<TEnum, string> Build<TEnum>(params (TEnum Value, string Text)[] mappings)
        where TEnum : struct, Enum
    {
        var toDb = mappings.ToDictionary(m => m.Value, m => m.Text);
        var fromDb = mappings.ToDictionary(m => m.Text, m => m.Value);

        // Dictionary lookups are valid expression trees. EF applies the compiled delegate
        // to enum constants at query time, so it never has to translate the indexer to SQL.
        return new ValueConverter<TEnum, string>(v => toDb[v], s => fromDb[s]);
    }
}
