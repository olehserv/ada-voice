using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests;

public class PhraseLibraryServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-svc-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Add_assigns_a_prefixed_id_writes_audio_first_and_persists()
    {
        var service = new PhraseLibraryService(new JsonPhraseRepository(_root));

        string? writtenFileName = null;
        var entry = service.Add("Take one", "c-default", durationMs: 1200, gainDb: -3.5,
            writeAudio: fileName => writtenFileName = fileName);

        Assert.StartsWith("p-", entry.Id);
        Assert.Equal(entry.Id + ".wav", entry.FileName);
        Assert.Equal(entry.FileName, writtenFileName); // writeAudio ran with the resolved file name
        Assert.Equal(-3.5, entry.GainDb, precision: 4);

        var reloaded = new PhraseLibraryService(new JsonPhraseRepository(_root));
        var persisted = Assert.Single(reloaded.Phrases);
        Assert.Equal(entry.Id, persisted.Id);
        Assert.Equal("Take one", persisted.Title);
    }

    [Fact]
    public void A_failed_audio_write_catalogues_nothing()
    {
        var service = new PhraseLibraryService(new JsonPhraseRepository(_root));

        Assert.Throws<IOException>(() =>
            service.Add("bad", "c-default", 100, 0, _ => throw new IOException("disk full")));

        Assert.Empty(new PhraseLibraryService(new JsonPhraseRepository(_root)).Phrases);
    }

    [Fact]
    public void Each_added_phrase_gets_a_distinct_id_and_increasing_sort_order()
    {
        var service = new PhraseLibraryService(new JsonPhraseRepository(_root));

        var a = service.Add("A", "c-default", 100, 0, _ => { });
        var b = service.Add("B", "c-default", 100, 0, _ => { });

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
