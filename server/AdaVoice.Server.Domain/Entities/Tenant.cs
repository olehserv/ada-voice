using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>A customer company. Owns users, subscriptions, devices, invoices, and usage.
/// See docs/monetize/database-design.md §2 "tenants".</summary>
public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TenantStatus Status { get; set; }
    public string ContactEmail { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
