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
    private readonly Library _library;

    public PhraseLibraryService(IPhraseRepository repository)
    {
        _repository = repository;
        _library = repository.Load();
    }

    public IReadOnlyList<PhraseEntry> Phrases => _library.Phrases;
    public IReadOnlyList<Category> Categories => _library.Categories;

    /// <summary>Catalogue a newly recorded take and persist the library. Returns the stored entry.</summary>
    public PhraseEntry Add(string title, string categoryId, string fileName, int durationMs, double gainDb)
    {
        var now = DateTime.UtcNow;
        var entry = new PhraseEntry
        {
            Id = NewId(),
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

    private static string NewId() => "p-" + Guid.NewGuid().ToString("N")[..8];
}
