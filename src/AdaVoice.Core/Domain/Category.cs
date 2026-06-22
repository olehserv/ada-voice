namespace AdaVoice.Core.Domain;

/// <summary>A grouping for phrases (design 04 §1). Colour is a hex string for the UI.</summary>
public sealed record Category
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Color { get; init; } = "";
    public int SortOrder { get; init; }
}
