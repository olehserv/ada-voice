using AdaVoice.Server.Domain.Abstractions;

namespace AdaVoice.Server.Domain.Entities;

/// <summary>Append-only usage history for a device activation.
/// See docs/monetize/database-design.md §2 "usage_events".</summary>
public class UsageEvent : IHasTimestamps, IHasTenant
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceActivationId { get; set; }

    /// <summary>e.g. phrase_played, app_started.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Raw JSON event payload.</summary>
    public string? Data { get; set; }

    /// <summary>Client clock (untrusted, informational).</summary>
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Server clock (trusted).</summary>
    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
