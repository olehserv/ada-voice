namespace AdaVoice.Core.Domain;

/// <summary>A grouping for phrases (design 04 §1). Colour is a hex string for the UI.</summary>
public sealed record Category
{
    /// <summary>Id of the seeded "Uncategorized" category — the fallback every phrase can fall back to,
    /// and the one category that cannot be deleted.</summary>
    public const string DefaultId = "c-default";

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Color { get; init; } = "";
    public int SortOrder { get; init; }
}
