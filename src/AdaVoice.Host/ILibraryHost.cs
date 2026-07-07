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

    /// <summary>The tag registry: each tag name and its colour. Used for tag suggestions and to colour
    /// the tag chips on the board.</summary>
    IReadOnlyList<TagInfo> Tags { get; }

    /// <summary>The conversations (ordered phrase scripts), in sort order.</summary>
    IReadOnlyList<Conversation> Conversations { get; }

    /// <summary>Ids of phrases whose audio file is missing — flagged broken in the UI rather than
    /// crashing playback.</summary>
    IReadOnlyList<string> BrokenPhraseIds { get; }

    /// <summary>Operator-readable warning about how the library loaded (locked file, corrupt file,
    /// restored backup), or null when the load was clean. The board shows it at startup so an empty
    /// board is never mistaken for an empty library (design 04 §3).</summary>
    string? LibraryWarning { get; }

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

    /// <summary>Create a conversation (no phrases yet). Throws if the name is blank.</summary>
    Conversation AddConversation(string name);

    /// <summary>Rename a conversation. Returns the updated conversation, or null if the id is unknown.
    /// Throws if the new name is blank.</summary>
    Conversation? RenameConversation(string id, string name);

    /// <summary>Delete a conversation. Its phrases are untouched. Returns false if the id is
    /// unknown.</summary>
    bool DeleteConversation(string id);

    /// <summary>Replace a conversation's ordered phrase list. Unknown phrase ids are dropped. Returns
    /// the updated conversation, or null if the id is unknown.</summary>
    Conversation? SetConversationPhrases(string id, IReadOnlyList<string> phraseIds);
}
