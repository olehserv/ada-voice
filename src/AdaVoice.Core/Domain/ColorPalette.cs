namespace AdaVoice.Core.Domain;

/// <summary>
/// The curated set of category/tag colours the UI offers. Colours are stored as data (hex strings) on
/// <see cref="Category.Color"/> and, later, on tags — so the palette lives with the domain that draws
/// from it, not in the UI. Chosen to read well on the dark board background and to be distinguishable
/// from each other.
/// </summary>
public static class ColorPalette
{
    /// <summary>The offered swatches, in display order.</summary>
    public static IReadOnlyList<string> Swatches { get; } =
    [
        "#4CC2FF", // accent blue
        "#54D262", // green
        "#F2A33C", // amber
        "#FF6B6B", // red
        "#B98AFF", // purple
        "#4FD1C5", // teal
        "#F06595", // pink
        "#FFD43B", // yellow
    ];
}
