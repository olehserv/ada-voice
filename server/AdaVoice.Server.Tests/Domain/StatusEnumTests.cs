using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Tests.Domain;

// Phase 1 status enums must have exactly the canonical members from
// docs/monetize/database-design.md §4 (C# PascalCase names). The text mapping
// (e.g. PastDue -> "past_due") happens via EF value converters in Task 2 — this
// test only pins the enum member set itself.
public class StatusEnumTests
{
    [Fact]
    public void TenantStatus_has_the_four_canonical_members() =>
        Assert.Equal(
            new[] { "Active", "Suspended", "Cancelled", "Deleted" },
            Enum.GetNames<TenantStatus>());

    [Fact]
    public void UserRole_has_the_three_canonical_members() =>
        Assert.Equal(
            new[] { "Operator", "TenantAdmin", "SuperAdmin" },
            Enum.GetNames<UserRole>());

    [Fact]
    public void UserStatus_has_the_two_canonical_members() =>
        Assert.Equal(
            new[] { "Active", "Disabled" },
            Enum.GetNames<UserStatus>());

    [Fact]
    public void SubscriptionStatus_has_the_seven_canonical_members() =>
        Assert.Equal(
            new[] { "Trial", "Active", "PastDue", "GracePeriod", "Suspended", "Cancelled", "Expired" },
            Enum.GetNames<SubscriptionStatus>());

    [Fact]
    public void DeviceStatus_has_the_four_canonical_members() =>
        Assert.Equal(
            new[] { "Active", "Revoked", "Blocked", "Expired" },
            Enum.GetNames<DeviceStatus>());

    [Fact]
    public void InvoiceStatus_has_the_six_canonical_members() =>
        Assert.Equal(
            new[] { "Draft", "Issued", "Paid", "Overdue", "Cancelled", "Refunded" },
            Enum.GetNames<InvoiceStatus>());

    [Fact]
    public void PaymentProvider_has_the_four_canonical_members() =>
        Assert.Equal(
            new[] { "ManualBankTransfer", "LiqPay", "WayForPay", "Fondy" },
            Enum.GetNames<PaymentProvider>());

    [Fact]
    public void LicenseTicketStatus_has_the_two_canonical_members() =>
        Assert.Equal(
            new[] { "Issued", "Revoked" },
            Enum.GetNames<LicenseTicketStatus>());

    [Fact]
    public void SigningKeyStatus_has_the_three_canonical_members() =>
        Assert.Equal(
            new[] { "Active", "Next", "Retired" },
            Enum.GetNames<SigningKeyStatus>());

    [Fact]
    public void ActorType_has_the_three_canonical_members() =>
        Assert.Equal(
            new[] { "User", "System", "Admin" },
            Enum.GetNames<ActorType>());
}
