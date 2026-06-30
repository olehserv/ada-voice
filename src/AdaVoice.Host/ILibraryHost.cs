using AdaVoice.Core.Domain;

namespace AdaVoice.Host;

/// <summary>
/// The library read-model and the edits the Board makes to it: the phrases and categories to show, the
/// ids of phrases whose audio is missing, and the phrase/category mutations. Kept behind a seam (like
/// <see cref="IPlaybackHost"/> / <see cref="IRecorderHost"/> / <see cref="ISettingsHost"/>) so the
/// view-models are unit-testable with a fake. <see cref="EngineHost"/> implements it.
/// </summary>
public interface ILibraryHost
{
    /// <summary>The catalogued phrases, in stored order.</summary>
    IReadOnlyList<PhraseEntry> Phrases { get; }

    /// <summary>The categories, in sort order (the seeded "Uncategorized" is always present).</summary>
    IReadOnlyList<Category> Categories { get; }

    /// <summary>Ids of phrases whose audio file is missing — flagged broken in the UI rather than
    /// crashing playback.</summary>
    IReadOnlyList<string> BrokenPhraseIds { get; }

    /// <summary>Rename a phrase. Returns the updated entry, or null if the id is unknown.</summary>
    PhraseEntry? SetPhraseTitle(string phraseId, string title);

    /// <summary>Move a phrase to another category. Returns the updated entry, or null if the phrase or
    /// the target category is unknown.</summary>
    PhraseEntry? SetPhraseCategory(string phraseId, string categoryId);

    /// <summary>Replace a phrase's tags with a normalized set. Returns the updated entry, or null if the
    /// id is unknown.</summary>
    PhraseEntry? SetPhraseTags(string phraseId, IEnumerable<string> tags);

    /// <summary>Delete a phrase by orphaning its WAV (never destroyed). Returns the removed entry, or
    /// null if the id is unknown.</summary>
    PhraseEntry? DeleteEntry(PhraseEntry entry);

    /// <summary>Create a category. Throws if the name is blank.</summary>
    Category AddCategory(string name, string color);

    /// <summary>Rename/recolour a category. Returns the updated category, or null if the id is unknown.
    /// Throws if the name is blank.</summary>
    Category? UpdateCategory(string id, string name, string color);

    /// <summary>Delete a category (its phrases fall back to Uncategorized). Returns false if the id is
    /// unknown or is the protected default category.</summary>
    bool DeleteCategory(string id);
}
