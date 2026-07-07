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

    /// <summary>Ordered phrase scripts for specific call types. Additive field (like <see cref="Tags"/>
    /// before it) — an older library file simply has none, so this defaults to empty rather than
    /// bumping <see cref="Version"/> (design: docs/superpowers/specs/2026-07-06-conversations-design.md §2).</summary>
    public List<Conversation> Conversations { get; init; } = [];
}
