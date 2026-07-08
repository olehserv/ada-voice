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
    public void Save_then_load_roundtrips_phrase_versions_and_the_conversation_random_flag()
    {
        var repo = new JsonPhraseRepository(_root);
        var library = repo.Load().Library;
        library.Phrases.Add(new PhraseEntry
        {
            Id = "p-1",
            FileName = "p-1.wav",
            Versions =
            [
                new PhraseVersion
                {
                    Id = "pv-1",
                    Label = "Friendly",
                    FileName = "p-1-pv-1.wav",
                    DurationMs = 900,
                    GainDb = -2.1,
                    CreatedAt = new DateTime(2026, 7, 7, 0, 0, 0, DateTimeKind.Utc),
                },
            ],
        });
        library.Conversations.Add(new Conversation { Id = "v-1", Name = "Script", UseRandomVersion = true });
        repo.Save(library);

        var reloaded = new JsonPhraseRepository(_root).Load().Library;

        var phrase = Assert.Single(reloaded.Phrases);
        var version = Assert.Single(phrase.Versions);
        Assert.Equal("pv-1", version.Id);
        Assert.Equal("Friendly", version.Label);
        Assert.Equal(-2.1, version.GainDb, precision: 4);

        var conversation = Assert.Single(reloaded.Conversations);
        Assert.True(conversation.UseRandomVersion);
    }

    // Additive-field backward compat (like Conversations before it): an older library.json simply
    // lacks these keys, and must load with safe defaults rather than failing to parse.
    [Fact]
    public void Old_format_json_without_versions_or_the_random_flag_loads_with_safe_defaults()
    {
        WriteLibrary("""
            {"version":1,"categories":[],"phrases":[{"id":"p-1","title":"","categoryId":"","tags":[],"fileName":"p-1.wav","durationMs":0,"gainDb":0,"sortOrder":0,"createdAt":"2026-07-01T00:00:00Z","updatedAt":"2026-07-01T00:00:00Z"}],"tags":[],"conversations":[{"id":"v-1","name":"Script","phraseIds":[],"sortOrder":0,"createdAt":"2026-07-01T00:00:00Z","updatedAt":"2026-07-01T00:00:00Z"}]}
            """);

        var result = new JsonPhraseRepository(_root).Load();

        Assert.Equal(LibraryLoadStatus.Loaded, result.Status);
        var phrase = Assert.Single(result.Library.Phrases);
        Assert.Empty(phrase.Versions);
        var conversation = Assert.Single(result.Library.Conversations);
        Assert.False(conversation.UseRandomVersion);
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

    [Fact]
    public void An_interrupted_save_leaves_the_previous_library_fully_intact()
    {
        // "kill-9" simulation: the atomic replace (temp -> rename over library.json) is prevented at
        // the last step, exactly as a crash mid-replace looks to the next reader. The previous good
        // library must survive whole — never half-written. (Replace-while-open is Windows semantics.)
        if (!OperatingSystem.IsWindows())
            return;

        var repo = new JsonPhraseRepository(_root);
        var library = repo.Load().Library;
        library.Phrases.Add(new PhraseEntry { Id = "p-keep", FileName = "p-keep.wav" });
        repo.Save(library); // a known-good library.json on disk

        var libraryFile = AdaVoicePaths.LibraryFile(_root);
        using (new FileStream(libraryFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Open without FILE_SHARE_DELETE, so the atomic replace cannot complete.
            library.Phrases.Add(new PhraseEntry { Id = "p-new", FileName = "p-new.wav" });
            // The save must fail loudly (IOException or UnauthorizedAccessException, OS-dependent),
            // never silently corrupt — the type doesn't matter, the integrity below does.
            Assert.NotNull(Record.Exception(() => repo.Save(library)));
        }

        var reloaded = new JsonPhraseRepository(_root).Load();
        Assert.Equal(LibraryLoadStatus.Loaded, reloaded.Status);
        Assert.Equal("p-keep", Assert.Single(reloaded.Library.Phrases).Id); // old content, whole
        Assert.False(File.Exists(libraryFile + ".tmp"));                    // failed save cleaned its temp
    }

    [Fact]
    public void A_leftover_temp_file_from_a_crash_does_not_affect_load()
    {
        WriteLibrary("{\"version\":1,\"categories\":[],\"phrases\":[]}");
        // A half-written temp left by a crash before the rename — must be ignored, never read.
        File.WriteAllText(AdaVoicePaths.LibraryFile(_root) + ".tmp", "{ half-written gar");

        var result = new JsonPhraseRepository(_root).Load();

        Assert.Equal(LibraryLoadStatus.Loaded, result.Status);
        Assert.Empty(result.Library.Phrases);
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
