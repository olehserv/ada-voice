namespace AdaVoice.Core.Domain;

/// <summary>
/// The curated set of category/tag colours the UI offers. Colours are stored as data (hex strings) on
/// <see cref="Category.Color"/> and, later, on tags — so the palette lives with the domain that draws
/// from it, not in the UI. Chosen to read well on the dark board background and to be distinguishable
/// from each other.
/// </summary>
public static class ColorPalette
{
    /// <summary>The offered colours, in display order. Includes the seeded default's grey so it shows
    /// as a real selection in the picker.</summary>
    public static IReadOnlyList<string> Swatches { get; } =
    [
        "#4CC2FF", // accent blue
        "#3B82F6", // blue
        "#4F46E5", // indigo
        "#B98AFF", // purple
        "#9333EA", // violet
        "#F06595", // pink
        "#EC4899", // magenta
        "#FF6B6B", // red
        "#E03131", // deep red
        "#F2A33C", // amber
        "#FF9F1C", // orange
        "#FFD43B", // yellow
        "#54D262", // green
        "#2F9E44", // deep green
        "#4FD1C5", // teal
        "#0CA678", // emerald
        "#22B8CF", // cyan
        "#A9B4C0", // slate
        "#808080", // grey (default category)
        "#5C5F66", // dark grey
    ];
}
