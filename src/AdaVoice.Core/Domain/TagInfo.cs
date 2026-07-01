namespace AdaVoice.Core.Domain;

/// <summary>
/// A tag in the library's tag registry: a name and the colour it shows in. Tags are stored on phrases
/// as plain name strings; this registry maps each name to one colour so the same tag looks the same
/// everywhere. Same init-property shape as <see cref="Category"/>, which round-trips through the
/// camelCase library JSON.
/// </summary>
public sealed record TagInfo
{
    public string Name { get; init; } = "";
    public string Color { get; init; } = "";
}
