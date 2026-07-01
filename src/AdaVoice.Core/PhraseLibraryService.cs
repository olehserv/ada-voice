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

    private void Load()
    {
        var result = _repository.Load();
        _library = result.Library;
        LoadStatus = result.Status;
        LoadDetail = result.Detail;
        BrokenPhraseIds = LibraryValidator.FindBrokenPhraseIds(_library, _audioExists);

        // One-time migration: give a colour to any tag that predates the registry (libraries written
        // before tags were coloured). Idempotent — once persisted, later loads find nothing to add.
        // Gated to a normal, fully-parsed load: ReadError returns an empty in-memory stand-in for a
        // good-but-locked file specifically so it is never overwritten, and RecoveredFromBackup already
        // persists itself. Migrating+saving on either path would defeat that safety.
        if (LoadStatus == LibraryLoadStatus.Loaded && RegisterTags(_library.Phrases.SelectMany(p => p.Tags)))
            _repository.Save(_library);
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
        var entry = _library.Phrases.FirstOrDefault(p => p.Id == phraseId);
        if (entry is null)
            return null;

        _library.Phrases.Remove(entry);
        _repository.Save(_library);

        orphanAudio(entry.FileName, "deleted-" + entry.FileName);
        return entry;
    }

    // ---- Categories ----------------------------------------------------------------------------

    /// <summary>Create a category at the end of the list and persist. Throws if the name is blank.</summary>
    public Category AddCategory(string name, string color)
    {
        var trimmed = RequireName(name);
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
        var index = _library.Categories.FindIndex(c => c.Id == id);
        if (index < 0)
            return null;

        var updated = _library.Categories[index] with { Name = RequireName(name), Color = color };
        _library.Categories[index] = updated;
        _repository.Save(_library);
        return updated;
    }

    /// <summary>Delete a category and move its phrases to Uncategorized (never lose a phrase). Returns
    /// false if the id is unknown or is the protected default category.</summary>
    public bool DeleteCategory(string id)
    {
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

    private PhraseEntry? EditPhrase(string phraseId, Func<PhraseEntry, PhraseEntry> edit)
    {
        var index = _library.Phrases.FindIndex(p => p.Id == phraseId);
        if (index < 0)
            return null;

        var updated = edit(_library.Phrases[index]) with { UpdatedAt = DateTime.UtcNow };
        _library.Phrases[index] = updated;
        _repository.Save(_library);
        return updated;
    }

    private static string RequireName(string name)
    {
        var trimmed = name?.Trim() ?? "";
        if (trimmed.Length == 0)
            throw new ArgumentException("A category name is required.", nameof(name));
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
