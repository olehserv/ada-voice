using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests.Storage;

public class JsonPhraseRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-core-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_file_loads_a_seeded_default()
    {
        var library = new JsonPhraseRepository(_root).Load();

        Assert.Equal(1, library.Version);
        Assert.Single(library.Categories);
        Assert.Empty(library.Phrases);
    }

    [Fact]
    public void Save_then_load_roundtrips_all_fields()
    {
        var repo = new JsonPhraseRepository(_root);
        var library = repo.Load();
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
        var p = Assert.Single(reloaded.Phrases);
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
        repo.Save(repo.Load());

        var libraryFile = AdaVoicePaths.LibraryFile(_root);
        Assert.True(File.Exists(libraryFile));
        Assert.False(File.Exists(libraryFile + ".tmp"));
        // Parses back without error.
        new JsonPhraseRepository(_root).Load();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
