namespace AdaVoice.Core.Domain;

/// <summary>An ordered, named group of existing phrases for a specific call script (design:
/// docs/superpowers/specs/2026-07-06-conversations-design.md). A phrase can belong to more than one
/// conversation; order lives here (in <see cref="PhraseIds"/>), not on the phrase, since the same
/// phrase can be a different step in each conversation that references it.</summary>
public sealed record Conversation
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Phrase ids in call order — index 0 is step one. Every id here must reference an
    /// existing <see cref="PhraseEntry"/>; a deleted phrase is pruned from this list (never left
    /// dangling — see <c>PhraseLibraryService.Delete</c>).</summary>
    public IReadOnlyList<string> PhraseIds { get; init; } = [];

    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    /// <summary>When true, playing a phrase as a step in this conversation picks uniformly at random
    /// from the phrase's primary recording plus all of its versions, instead of always the primary
    /// (plan: docs/superpowers/plans/2026-07-07-phrase-versions.md).</summary>
    public bool UseRandomVersion { get; init; }
}
