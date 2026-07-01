namespace AdaVoice.Core.Domain;

/// <summary>The whole phrase library as stored in <c>library.json</c> (design 04 §1).</summary>
public sealed record Library
{
    public int Version { get; init; } = 1;
    public List<Category> Categories { get; init; } = [];
    public List<PhraseEntry> Phrases { get; init; } = [];

    /// <summary>The tag registry: one colour per tag name. Phrases store tag names; this gives each name
    /// a stable colour. Grows as tags are used (see <c>PhraseLibraryService.SetPhraseTags</c>).</summary>
    public List<TagInfo> Tags { get; init; } = [];
}
