using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core;

/// <summary>
/// The application's view of the phrase library: loads it once via an <see cref="IPhraseRepository"/>,
/// exposes the phrases/categories, and persists changes. Keeps the repository (storage detail) behind
/// the service so the host and future UI talk to this.
/// </summary>
public sealed class PhraseLibraryService
{
    private readonly IPhraseRepository _repository;
    private readonly Func<string, bool> _audioExists;
    private Library _library = new(); // replaced by Load() in the constructor

    /// <param name="repository">The store the library is loaded from and saved to.</param>
    /// <param name="audioExists">Tells whether a phrase's WAV (by file name) exists, used to flag
    /// broken phrases at startup. Injected so the service stays testable without disk; defaults to
    /// "assume present" so call sites that don't care are unaffected.</param>
    public PhraseLibraryService(IPhraseRepository repository, Func<string, bool>? audioExists = null)
    {
        _repository = repository;
        _audioExists = audioExists ?? (_ => true);
        Load();
    }

    public IReadOnlyList<PhraseEntry> Phrases => _library.Phrases;
    public IReadOnlyList<Category> Categories => _library.Categories;

    /// <summary>The tag registry: each tag name and the colour it shows in. Grows as tags are used.</summary>
    public IReadOnlyList<TagInfo> Tags => _library.Tags;

    /// <summary>The conversations (ordered phrase scripts), in sort order.</summary>
    public IReadOnlyList<Conversation> Conversations => _library.Conversations;

    /// <summary>How the library was loaded — the host/UI surfaces this so a corrupt file is never
    /// mistaken for an empty library (design 04 §3).</summary>
    public LibraryLoadStatus LoadStatus { get; private set; }

    /// <summary>Extra detail for <see cref="LoadStatus"/> when something went wrong (else null).</summary>
    public string? LoadDetail { get; private set; }

    /// <summary>Ids of phrases whose audio file is missing — to be flagged broken in the UI rather
    /// than crashing playback. Runtime-only; never persisted.</summary>
    public IReadOnlyList<string> BrokenPhraseIds { get; private set; } = [];

    /// <summary>Re-read the library from storage, discarding the in-memory copy. Used after an
    /// operation changes the store underneath the service (e.g. an import) so the running session
    /// reflects the new state without a restart.</summary>
    public void Reload() => Load();

    /// <summary>False while the load failed transiently (<see cref="LibraryLoadStatus.ReadError"/>):
    /// the in-memory library is then an empty stand-in for a good-but-locked file, and persisting any
    /// change would overwrite the real library with it (design 04 §3). Every mutator refuses in this
    /// state; a successful <see cref="Reload"/> clears it.</summary>
    public bool IsWritable => LoadStatus != LibraryLoadStatus.ReadError;

    private void EnsureWritable()
    {
        if (!IsWritable)
            throw new InvalidOperationException(
                "The phrase library could not be read (another program may be holding the file), " +
                "so changes are disabled to protect it. Restart AdaVoice or try again later.");
    }

    private void Load()
    {
        var result = _repository.Load();
        _library = result.Library;
        LoadStatus = result.Status;
        LoadDetail = result.Detail;
        BrokenPhraseIds = LibraryValidator.FindBrokenPhraseIds(_library, _audioExists);

        // One-time migrations: give a colour to any tag that predates the registry, and drop any
        // conversation reference to a phrase that no longer exists (e.g. a hand-edited or
        // merge-imported library). Gated to a normal, fully-parsed load: ReadError returns an empty
        // in-memory stand-in for a good-but-locked file specifically so it is never overwritten, and
        // RecoveredFromBackup already persists itself. Migrating+saving on either path would defeat
        // that safety.
        if (LoadStatus == LibraryLoadStatus.Loaded)
        {
            var tagsChanged = RegisterTags(_library.Phrases.SelectMany(p => p.Tags));
            var conversationsChanged = PruneUnknownConversationPhrases();
            if (tagsChanged || conversationsChanged)
                _repository.Save(_library);
        }
    }

    /// <summary>Drop any phrase id from a conversation's step list that no longer matches an existing
    /// phrase. Returns true if anything changed. Does not persist — the caller (<see cref="Load"/>)
    /// saves once for both migrations.</summary>
    private bool PruneUnknownConversationPhrases()
    {
        var knownIds = _library.Phrases.Select(p => p.Id).ToHashSet();
        var changed = false;
        for (var i = 0; i < _library.Conversations.Count; i++)
        {
            var conversation = _library.Conversations[i];
            var filtered = conversation.PhraseIds.Where(knownIds.Contains).ToList();
            if (filtered.Count == conversation.PhraseIds.Count)
                continue;

            _library.Conversations[i] = conversation with { PhraseIds = filtered, UpdatedAt = DateTime.UtcNow };
            changed = true;
        }

        return changed;
    }

    /// <summary>Ensure each name has a registry entry, assigning the next palette colour (cycling) to
    /// any that is new. Case-insensitive: "Opening" and "opening" share one entry. Returns true if the
    /// registry changed. Does not persist — the caller decides when to save.</summary>
    private bool RegisterTags(IEnumerable<string> names)
    {
        var changed = false;
        foreach (var name in names)
        {
            if (_library.Tags.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;

            var color = ColorPalette.Swatches[_library.Tags.Count % ColorPalette.Swatches.Count];
            _library.Tags.Add(new TagInfo { Name = name, Color = color });
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Catalogue a newly recorded take and persist the library. The id (and the
    /// <c>{id}.wav</c> file name) are generated here; <paramref name="writeAudio"/> is called with
    /// that file name to write the WAV <b>before</b> the metadata is persisted — so a write failure
    /// leaves nothing catalogued, and a later metadata failure leaves only an orphan WAV (design 04).
    /// </summary>
    public PhraseEntry Add(string title, string categoryId, int durationMs, double gainDb, Action<string> writeAudio)
    {
        EnsureWritable();
        var id = NewId();
        var fileName = $"{id}.wav";
        writeAudio(fileName);

        var now = DateTime.UtcNow;
        var entry = new PhraseEntry
        {
            Id = id,
            Title = title,
            CategoryId = categoryId,
            Tags = [],
            FileName = fileName,
            DurationMs = durationMs,
            GainDb = gainDb,
            SortOrder = _library.Phrases.Count,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _library.Phrases.Add(entry);
        _repository.Save(_library);
        return entry;
    }

    /// <summary>
    /// Delete a phrase by orphaning it: remove the metadata entry, persist, then ask the caller to
    /// rename its WAV to <c>deleted-{id}.wav</c> in place. Voice recordings are irreplaceable, so the
    /// file is never destroyed (design 04 §3). Metadata is removed and persisted <b>before</b> the
    /// rename, so if the rename fails the library stays consistent and the take survives as
    /// <c>{id}.wav</c>. <paramref name="orphanAudio"/> is called with (current file name, orphan file
    /// name); file I/O lives with the caller, mirroring <see cref="Add"/>. Returns the removed entry,
    /// or null if no phrase has that id.
    /// </summary>
    public PhraseEntry? Delete(string phraseId, Action<string, string> orphanAudio)
    {
        EnsureWritable();
        var entry = _library.Phrases.FirstOrDefault(p => p.Id == phraseId);
        if (entry is null)
            return null;

        _library.Phrases.Remove(entry);
        PruneConversationPhrase(phraseId);
        _repository.Save(_library);

        orphanAudio(entry.FileName, "deleted-" + entry.FileName);
        return entry;
    }

    /// <summary>Remove one phrase id from every conversation's step list — a deleted phrase can no
    /// longer be referenced (design: docs/superpowers/specs/2026-07-06-conversations-design.md §2).
    /// Does not persist; the caller saves.</summary>
    private void PruneConversationPhrase(string phraseId)
    {
        for (var i = 0; i < _library.Conversations.Count; i++)
        {
            var conversation = _library.Conversations[i];
            if (!conversation.PhraseIds.Contains(phraseId))
                continue;

            _library.Conversations[i] = conversation with
            {
                PhraseIds = conversation.PhraseIds.Where(id => id != phraseId).ToList(),
                UpdatedAt = DateTime.UtcNow,
            };
        }
    }

    // ---- Categories ----------------------------------------------------------------------------

    /// <summary>Create a category at the end of the list and persist. Throws if the name is blank.</summary>
    public Category AddCategory(string name, string color)
    {
        EnsureWritable();
        var trimmed = RequireName(name, "category");
        var category = new Category
        {
            Id = "c-" + Guid.NewGuid().ToString("N")[..8],
            Name = trimmed,
            Color = color,
            SortOrder = _library.Categories.Count,
        };

        _library.Categories.Add(category);
        _repository.Save(_library);
        return category;
    }

    /// <summary>Rename and recolour a category. Returns the updated category, or null if no category has
    /// that id. Throws if the new name is blank.</summary>
    public Category? UpdateCategory(string id, string name, string color)
    {
        EnsureWritable();
        var index = _library.Categories.FindIndex(c => c.Id == id);
        if (index < 0)
            return null;

        var updated = _library.Categories[index] with { Name = RequireName(name, "category"), Color = color };
        _library.Categories[index] = updated;
        _repository.Save(_library);
        return updated;
    }

    /// <summary>Delete a category and move its phrases to Uncategorized (never lose a phrase). Returns
    /// false if the id is unknown or is the protected default category.</summary>
    public bool DeleteCategory(string id)
    {
        EnsureWritable();
        if (id == Category.DefaultId)
            return false; // Uncategorized is the fallback and cannot be removed.

        var index = _library.Categories.FindIndex(c => c.Id == id);
        if (index < 0)
            return false;

        var now = DateTime.UtcNow;
        for (var i = 0; i < _library.Phrases.Count; i++)
            if (_library.Phrases[i].CategoryId == id)
                _library.Phrases[i] = _library.Phrases[i] with { CategoryId = Category.DefaultId, UpdatedAt = now };

        _library.Categories.RemoveAt(index);
        _repository.Save(_library);
        return true;
    }

    // ---- Conversations -------------------------------------------------------------------------

    /// <summary>Create a conversation at the end of the list (no phrases yet) and persist. Throws if
    /// the name is blank.</summary>
    public Conversation AddConversation(string name)
    {
        EnsureWritable();
        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Id = "v-" + Guid.NewGuid().ToString("N")[..8],
            Name = RequireName(name, "conversation"),
            PhraseIds = [],
            SortOrder = _library.Conversations.Count,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _library.Conversations.Add(conversation);
        _repository.Save(_library);
        return conversation;
    }

    /// <summary>Rename a conversation. Returns the updated conversation, or null if no conversation
    /// has that id. Throws if the new name is blank.</summary>
    public Conversation? RenameConversation(string id, string name)
    {
        EnsureWritable();
        var index = _library.Conversations.FindIndex(c => c.Id == id);
        if (index < 0)
            return null;

        var updated = _library.Conversations[index] with { Name = RequireName(name, "conversation"), UpdatedAt = DateTime.UtcNow };
        _library.Conversations[index] = updated;
        _repository.Save(_library);
        return updated;
    }

    /// <summary>Delete a conversation. Its phrases are untouched — a conversation only references
    /// them. Returns false if the id is unknown.</summary>
    public bool DeleteConversation(string id)
    {
        EnsureWritable();
        var index = _library.Conversations.FindIndex(c => c.Id == id);
        if (index < 0)
            return false;

        _library.Conversations.RemoveAt(index);
        _repository.Save(_library);
        return true;
    }

    /// <summary>Replace a conversation's ordered phrase list. Unknown phrase ids are silently
    /// dropped — a conversation may only reference phrases that exist, the same invariant a deleted
    /// phrase's cleanup enforces (see <see cref="PruneConversationPhrase"/>). Returns the updated
    /// conversation, or null if no conversation has that id.</summary>
    public Conversation? SetConversationPhrases(string id, IReadOnlyList<string> phraseIds)
    {
        EnsureWritable();
        var index = _library.Conversations.FindIndex(c => c.Id == id);
        if (index < 0)
            return null;

        var knownIds = _library.Phrases.Select(p => p.Id).ToHashSet();
        var filtered = phraseIds.Where(knownIds.Contains).ToList();

        var updated = _library.Conversations[index] with { PhraseIds = filtered, UpdatedAt = DateTime.UtcNow };
        _library.Conversations[index] = updated;
        _repository.Save(_library);
        return updated;
    }

    // ---- Phrase edits --------------------------------------------------------------------------

    /// <summary>Rename a phrase. Returns the updated phrase, or null if no phrase has that id. Throws if
    /// the new title is blank.</summary>
    public PhraseEntry? SetPhraseTitle(string phraseId, string title)
    {
        var trimmed = RequireTitle(title);
        return EditPhrase(phraseId, p => p with { Title = trimmed });
    }

    /// <summary>Move a phrase to another category. Returns the updated phrase, or null if the phrase or
    /// the target category does not exist.</summary>
    public PhraseEntry? SetPhraseCategory(string phraseId, string categoryId)
    {
        if (_library.Categories.All(c => c.Id != categoryId))
            return null;

        return EditPhrase(phraseId, p => p with { CategoryId = categoryId });
    }

    /// <summary>Replace a phrase's tags with a normalized set (trimmed, no blanks, de-duplicated
    /// case-insensitively, order preserved). Returns the updated phrase, or null if not found.</summary>
    public PhraseEntry? SetPhraseTags(string phraseId, IEnumerable<string> tags)
    {
        var normalized = tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Register new names before editing, but only once we know the phrase exists (so a no-op edit
        // never leaves orphan registry entries). EditPhrase then saves both in one write.
        if (_library.Phrases.All(p => p.Id != phraseId))
            return null;

        RegisterTags(normalized);
        return EditPhrase(phraseId, p => p with { Tags = normalized });
    }

    // All three public phrase edits funnel through here, so one guard covers them.
    private PhraseEntry? EditPhrase(string phraseId, Func<PhraseEntry, PhraseEntry> edit)
    {
        EnsureWritable();
        var index = _library.Phrases.FindIndex(p => p.Id == phraseId);
        if (index < 0)
            return null;

        var updated = edit(_library.Phrases[index]) with { UpdatedAt = DateTime.UtcNow };
        _library.Phrases[index] = updated;
        _repository.Save(_library);
        return updated;
    }

    private static string RequireName(string name, string what)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new ArgumentException($"A {what} name is required.", nameof(name));
        return trimmed;
    }

    private static string RequireTitle(string title)
    {
        var trimmed = title?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new ArgumentException("A phrase title is required.", nameof(title));
        return trimmed;
    }

    private static string NewId() => "p-" + Guid.NewGuid().ToString("N")[..8];
}
