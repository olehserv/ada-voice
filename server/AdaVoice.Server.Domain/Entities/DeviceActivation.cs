using AdaVoice.Server.Domain.Abstractions;
using AdaVoice.Server.Domain.Enums;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>One desktop install of a user. Id is the ticket's <c>deviceActivationId</c>.
/// Unique per (tenant_id, device_id); re-activation updates the row.
/// See docs/monetize/database-design.md §2 "device_activations".</summary>
public class DeviceActivation : IHasTimestamps, IHasTenant
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }

    /// <summary>Client-generated GUID from device.bin.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>SHA-256 hex of soft machine signals.</summary>
    public string MachineHash { get; set; } = string.Empty;

    public DeviceStatus Status { get; set; }
    public DateTimeOffset ActivatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string AppVersion { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
