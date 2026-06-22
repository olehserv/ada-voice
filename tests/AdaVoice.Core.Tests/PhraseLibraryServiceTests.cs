using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests;

public class PhraseLibraryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-svc-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Add_assigns_a_prefixed_id_and_persists_so_a_fresh_service_sees_it()
    {
        var service = new PhraseLibraryService(new JsonPhraseRepository(_root));

        var entry = service.Add("Take one", "c-default", "p-x.wav", durationMs: 1200, gainDb: -3.5);

        Assert.StartsWith("p-", entry.Id);
        Assert.Equal(-3.5, entry.GainDb, precision: 4);

        var reloaded = new PhraseLibraryService(new JsonPhraseRepository(_root));
        var persisted = Assert.Single(reloaded.Phrases);
        Assert.Equal(entry.Id, persisted.Id);
        Assert.Equal("Take one", persisted.Title);
    }

    [Fact]
    public void Each_added_phrase_gets_a_distinct_id_and_increasing_sort_order()
    {
        var service = new PhraseLibraryService(new JsonPhraseRepository(_root));

        var a = service.Add("A", "c-default", "a.wav", 100, 0);
        var b = service.Add("B", "c-default", "b.wav", 100, 0);

        Assert.NotEqual(a.Id, b.Id);
        Assert.Equal(0, a.SortOrder);
        Assert.Equal(1, b.SortOrder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
