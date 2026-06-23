using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests.Storage;

public class JsonPhraseRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-core-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_file_loads_a_seeded_default()
    {
        var result = new JsonPhraseRepository(_root).Load();

        Assert.Equal(LibraryLoadStatus.SeededDefault, result.Status);
        Assert.Equal(1, result.Library.Version);
        Assert.Single(result.Library.Categories);
        Assert.Empty(result.Library.Phrases);
    }

    [Fact]
    public void Save_then_load_roundtrips_all_fields()
    {
        var repo = new JsonPhraseRepository(_root);
        var library = repo.Load().Library;
        library.Phrases.Add(new PhraseEntry
        {
            Id = "p-abc12345",
            Title = "Hello",
            CategoryId = "c-default",
            Tags = ["opening", "greeting"],
            FileName = "p-abc12345.wav",
            DurationMs = 2350,
            GainDb = -2.4,
            SortOrder = 0,
            CreatedAt = new DateTime(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 6, 23, 10, 0, 0, DateTimeKind.Utc),
        });
        repo.Save(library);

        var reloaded = new JsonPhraseRepository(_root).Load();
        Assert.Equal(LibraryLoadStatus.Loaded, reloaded.Status);
        var p = Assert.Single(reloaded.Library.Phrases);
        Assert.Equal("p-abc12345", p.Id);
        Assert.Equal("Hello", p.Title);
        Assert.Equal(["opening", "greeting"], p.Tags);
        Assert.Equal("p-abc12345.wav", p.FileName);
        Assert.Equal(2350, p.DurationMs);
        Assert.Equal(-2.4, p.GainDb, precision: 4);
    }

    [Fact]
    public void Save_writes_valid_json_and_leaves_no_temp_file()
    {
        var repo = new JsonPhraseRepository(_root);
        repo.Save(repo.Load().Library);

        var libraryFile = AdaVoicePaths.LibraryFile(_root);
        Assert.True(File.Exists(libraryFile));
        Assert.False(File.Exists(libraryFile + ".tmp"));
        // Parses back without error.
        new JsonPhraseRepository(_root).Load();
    }

    [Fact]
    public void Malformed_json_is_corrupt_and_the_bad_file_is_quarantined()
    {
        WriteLibrary("{ this is not valid json");

        var result = new JsonPhraseRepository(_root).Load();

        Assert.Equal(LibraryLoadStatus.Corrupt, result.Status);
        // Started with a seeded default, NOT an empty library, and did not throw.
        Assert.Single(result.Library.Categories);
        Assert.False(File.Exists(AdaVoicePaths.LibraryFile(_root))); // moved aside
        Assert.NotEmpty(QuarantineFiles());                         // preserved, not destroyed
    }

    [Fact]
    public void Empty_file_is_treated_as_corrupt_not_as_an_empty_library()
    {
        WriteLibrary("");

        var result = new JsonPhraseRepository(_root).Load();

        Assert.Equal(LibraryLoadStatus.Corrupt, result.Status);
        Assert.NotEmpty(QuarantineFiles());
    }

    [Fact]
    public void Literal_null_is_corrupt_so_we_never_start_silently_empty()
    {
        WriteLibrary("null");

        var result = new JsonPhraseRepository(_root).Load();

        Assert.Equal(LibraryLoadStatus.Corrupt, result.Status);
        Assert.NotEmpty(QuarantineFiles());
    }

    [Fact]
    public void Corrupt_library_recovers_from_backup_and_restores_the_file()
    {
        WriteLibrary("{ broken");
        var recovered = new Library();
        recovered.Phrases.Add(new PhraseEntry { Id = "p-restored", FileName = "p-restored.wav" });

        var result = new JsonPhraseRepository(_root, () => recovered).Load();

        Assert.Equal(LibraryLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equal("p-restored", Assert.Single(result.Library.Phrases).Id);
        Assert.NotEmpty(QuarantineFiles()); // the bad file is still preserved

        // Restored to disk, so a restart keeps it (this load has no recovery delegate).
        var reloaded = new JsonPhraseRepository(_root).Load();
        Assert.Equal(LibraryLoadStatus.Loaded, reloaded.Status);
        Assert.Equal("p-restored", Assert.Single(reloaded.Library.Phrases).Id);
    }

    [Fact]
    public void Corrupt_library_with_no_recoverable_backup_stays_corrupt()
    {
        WriteLibrary("{ broken");

        var result = new JsonPhraseRepository(_root, () => null).Load();

        Assert.Equal(LibraryLoadStatus.Corrupt, result.Status);
    }

    private void WriteLibrary(string content)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(AdaVoicePaths.LibraryFile(_root), content);
    }

    private string[] QuarantineFiles() =>
        Directory.Exists(_root) ? Directory.GetFiles(_root, "library.corrupt-*.json") : [];

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
