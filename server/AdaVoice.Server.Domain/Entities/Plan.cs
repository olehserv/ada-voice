namespace AdaVoice.Server.Domain.Entities;

/// <summary>A global price/limit template that subscriptions link a tenant to.
/// See docs/monetize/database-design.md §2 "plans".</summary>
public class Plan
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceUah { get; set; }
    public int MaxDevices { get; set; }
    public int MaxPhrases { get; set; }

    /// <summary>Raw JSON feature codes, e.g. ["phrase_library","hotkeys"].</summary>
    public string Features { get; set; } = string.Empty;

    public int TrialGraceDays { get; set; }
    public int PaidGraceDays { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
