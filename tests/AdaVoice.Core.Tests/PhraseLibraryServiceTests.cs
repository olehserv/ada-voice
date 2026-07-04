using AdaVoice.Core.Domain;
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

    [Fact]
    public void Broken_phrase_ids_reflect_the_audio_exists_predicate()
    {
        var seed = new PhraseLibraryService(new JsonPhraseRepository(_root));
        var entry = seed.Add("missing audio", "c-default", 100, 0, _ => { });

        var reloaded = new PhraseLibraryService(new JsonPhraseRepository(_root), audioExists: _ => false);

        Assert.Equal([entry.Id], reloaded.BrokenPhraseIds);
    }

    [Fact]
    public void Load_status_is_surfaced_from_the_repository()
    {
        var service = new PhraseLibraryService(new JsonPhraseRepository(_root));

        Assert.Equal(LibraryLoadStatus.SeededDefault, service.LoadStatus);
        Assert.Null(service.LoadDetail);
    }

    [Fact]
    public void Delete_removes_the_entry_persists_and_orphans_the_wav()
    {
        var service = new PhraseLibraryService(new JsonPhraseRepository(_root));
        var entry = service.Add("to delete", "c-default", 100, 0, _ => { });

        (string from, string to)? renamed = null;
        var deleted = service.Delete(entry.Id, (from, to) => renamed = (from, to));

        Assert.Equal(entry.Id, deleted!.Id);
        Assert.Equal((entry.FileName, "deleted-" + entry.FileName), renamed); // never destroyed, just renamed
        Assert.Empty(service.Phrases);
        Assert.Empty(new PhraseLibraryService(new JsonPhraseRepository(_root)).Phrases); // persisted
    }

    [Fact]
    public void Delete_unknown_id_is_a_noop_and_does_not_touch_audio()
    {
        var service = new PhraseLibraryService(new JsonPhraseRepository(_root));
        service.Add("keep", "c-default", 100, 0, _ => { });

        var orphaned = false;
        var result = service.Delete("p-nope", (_, _) => orphaned = true);

        Assert.Null(result);
        Assert.False(orphaned);
        Assert.Single(service.Phrases);
    }

    [Fact]
    public void Reload_picks_up_changes_made_to_the_store_underneath_it()
    {
        var repo = new JsonPhraseRepository(_root);
        var service = new PhraseLibraryService(repo);
        Assert.Empty(service.Phrases);

        // Change the store underneath the service, as an import does.
        var library = repo.Load().Library;
        library.Phrases.Add(new PhraseEntry { Id = "p-ext", FileName = "p-ext.wav" });
        repo.Save(library);

        Assert.Empty(service.Phrases); // stale until reloaded
        service.Reload();
        Assert.Equal("p-ext", Assert.Single(service.Phrases).Id);
    }

    // C2 regression: ReadError means the in-memory library is an empty stand-in for a good-but-locked
    // file. Every mutator must refuse — one Save would replace the operator's real library with it.
    [Fact]
    public void Mutators_refuse_while_the_library_is_a_read_error_stand_in_and_never_save()
    {
        var repo = new StubRepository(new LibraryLoadResult(new Library(), LibraryLoadStatus.ReadError, "locked"));
        var service = new PhraseLibraryService(repo);

        Assert.False(service.IsWritable);
        var audioWritten = false;
        Assert.Throws<InvalidOperationException>(() => service.Add("t", "c-default", 100, 0, _ => audioWritten = true));
        Assert.Throws<InvalidOperationException>(() => service.Delete("p-1", (_, _) => { }));
        Assert.Throws<InvalidOperationException>(() => service.AddCategory("Openers", "#ffffff"));
        Assert.Throws<InvalidOperationException>(() => service.UpdateCategory("c-x", "n", "#ffffff"));
        Assert.Throws<InvalidOperationException>(() => service.DeleteCategory("c-x"));
        Assert.Throws<InvalidOperationException>(() => service.SetPhraseTitle("p-1", "t"));

        Assert.False(audioWritten); // the guard runs before the WAV write, so nothing lands on disk
        Assert.Equal(0, repo.SaveCount);
    }

    [Fact]
    public void A_successful_reload_makes_the_library_writable_again()
    {
        var repo = new StubRepository(new LibraryLoadResult(new Library(), LibraryLoadStatus.ReadError, "locked"));
        var service = new PhraseLibraryService(repo);
        Assert.False(service.IsWritable);

        // The lock is gone: the next load succeeds and mutations work again.
        repo.Result = new LibraryLoadResult(new Library(), LibraryLoadStatus.Loaded);
        service.Reload();

        Assert.True(service.IsWritable);
        service.AddCategory("Openers", "#ffffff");
        Assert.Equal(1, repo.SaveCount);
    }

    private sealed class StubRepository(LibraryLoadResult result) : IPhraseRepository
    {
        public LibraryLoadResult Result { get; set; } = result;
        public int SaveCount { get; private set; }

        public LibraryLoadResult Load() => Result;
        public void Save(Library library) => SaveCount++;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
