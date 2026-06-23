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

    private static string NewId() => "p-" + Guid.NewGuid().ToString("N")[..8];
}
