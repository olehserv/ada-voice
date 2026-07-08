using System.IO.Compression;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests.Storage;

public class LibraryArchiveServiceTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "adavoice-arc-" + Guid.NewGuid().ToString("N"));

    public LibraryArchiveServiceTests() => Directory.CreateDirectory(_work);

    private string Root(string name) => Path.Combine(_work, name);
    private string Zip(string name) => Path.Combine(_work, name + ".zip");
    private static LibraryArchiveService Archive(string root) => new(root, new JsonPhraseRepository(root));

    private void Seed(string root, string phraseId, byte[] audio)
    {
        var repo = new JsonPhraseRepository(root);
        var library = repo.Load().Library;
        library.Phrases.Add(new PhraseEntry { Id = phraseId, FileName = phraseId + ".wav" });
        repo.Save(library);
        Directory.CreateDirectory(AdaVoicePaths.AudioDir(root));
        File.WriteAllBytes(AdaVoicePaths.AudioPath(root, phraseId + ".wav"), audio);
    }

    [Fact]
    public void Export_then_import_round_trips_metadata_and_audio()
    {
        var src = Root("src");
        Seed(src, "p-1", [1, 2, 3, 4]);
        var zip = Zip("export");
        Archive(src).Export(zip);

        var dest = Root("dest");
        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.True(result.Success);
        Assert.Equal(1, result.Added);
        var reloaded = new JsonPhraseRepository(dest).Load().Library;
        Assert.Equal("p-1", Assert.Single(reloaded.Phrases).Id);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(AdaVoicePaths.AudioPath(dest, "p-1.wav")));
    }

    [Fact]
    public void Export_excludes_orphaned_wavs()
    {
        var src = Root("src");
        Seed(src, "p-1", [1]);
        File.WriteAllBytes(AdaVoicePaths.AudioPath(src, "deleted-p-9.wav"), [9]); // orphan in audio\
        var zip = Zip("export");
        Archive(src).Export(zip);

        using var archive = ZipFile.OpenRead(zip);
        Assert.NotNull(archive.GetEntry("audio/p-1.wav"));
        Assert.Null(archive.GetEntry("audio/deleted-p-9.wav"));
    }

    [Fact]
    public void Export_drops_version_audio_and_reports_how_many()
    {
        var src = Root("src");
        var repo = new JsonPhraseRepository(src);
        var library = repo.Load().Library;
        library.Phrases.Add(new PhraseEntry
        {
            Id = "p-1",
            FileName = "p-1.wav",
            Versions = [new PhraseVersion { Id = "pv-1", FileName = "p-1-pv-1.wav" }],
        });
        repo.Save(library);
        Directory.CreateDirectory(AdaVoicePaths.AudioDir(src));
        File.WriteAllBytes(AdaVoicePaths.AudioPath(src, "p-1.wav"), [1]);
        File.WriteAllBytes(AdaVoicePaths.AudioPath(src, "p-1-pv-1.wav"), [2]);

        var zip = Zip("export");
        var dropped = Archive(src).Export(zip);

        Assert.Equal(1, dropped);
        using (var archive = ZipFile.OpenRead(zip))
        {
            Assert.NotNull(archive.GetEntry("audio/p-1.wav"));    // primary is exported
            Assert.Null(archive.GetEntry("audio/p-1-pv-1.wav")); // version audio is not (v1 limitation)
        }

        // The embedded metadata carries no version references either — an import must never see a
        // version it has no audio for.
        var dest = Root("dest");
        var result = Archive(dest).Import(zip, ImportMode.Merge);
        Assert.True(result.Success);
        var imported = Assert.Single(new JsonPhraseRepository(dest).Load().Library.Phrases);
        Assert.Empty(imported.Versions);
    }

    [Fact]
    public void Import_strips_a_crafted_archives_claimed_versions()
    {
        var dest = Root("dest");
        var zip = Zip("crafted-versions");
        WriteZipWithLibraryJson(zip, "{\"version\":1,\"categories\":[],\"phrases\":[" +
            "{\"id\":\"p-1\",\"fileName\":\"p-1.wav\",\"versions\":[{\"id\":\"pv-1\",\"fileName\":\"p-1-pv-1.wav\"}]}]}");

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.True(result.Success);
        var imported = Assert.Single(new JsonPhraseRepository(dest).Load().Library.Phrases);
        Assert.Empty(imported.Versions); // never staged — the archive never actually carried this audio
    }

    [Fact]
    public void Merge_skips_duplicate_ids_and_adds_new_ones()
    {
        var src = Root("src");
        Seed(src, "p-1", [1]);
        Seed(src, "p-2", [2]);
        var zip = Zip("export");
        Archive(src).Export(zip);

        var dest = Root("dest");
        Seed(dest, "p-1", [9]); // a different p-1 already present

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.True(result.Success);
        Assert.Equal(1, result.Added);   // p-2
        Assert.Equal(1, result.Skipped); // p-1 duplicate
        var library = new JsonPhraseRepository(dest).Load().Library;
        Assert.Equal(2, library.Phrases.Count);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(AdaVoicePaths.AudioPath(dest, "p-1.wav"))); // existing kept
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(AdaVoicePaths.AudioPath(dest, "p-2.wav"))); // new written
    }

    [Fact]
    public void Replace_swaps_the_whole_library()
    {
        var src = Root("src");
        Seed(src, "p-new", [5]);
        var zip = Zip("export");
        Archive(src).Export(zip);

        var dest = Root("dest");
        Seed(dest, "p-old", [9]);

        var result = Archive(dest).Import(zip, ImportMode.Replace);

        Assert.True(result.Success);
        var library = new JsonPhraseRepository(dest).Load().Library;
        Assert.Equal("p-new", Assert.Single(library.Phrases).Id);
        Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(AdaVoicePaths.AudioPath(dest, "p-new.wav")));
    }

    [Fact]
    public void Import_rejects_an_unsupported_version_and_changes_nothing()
    {
        var dest = Root("dest");
        Seed(dest, "p-keep", [7]);
        var zip = Zip("v2");
        WriteZipWithLibraryJson(zip, "{\"version\":2,\"categories\":[],\"phrases\":[]}");

        var result = Archive(dest).Import(zip, ImportMode.Replace);

        Assert.False(result.Success);
        Assert.Contains("version", result.Error!);
        Assert.Equal("p-keep", Assert.Single(new JsonPhraseRepository(dest).Load().Library.Phrases).Id);
    }

    [Fact]
    public void Import_rejects_an_archive_with_no_valid_library()
    {
        var dest = Root("dest");
        var zip = Zip("empty");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("readme.txt"); // no library.json at all

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.False(result.Success);
    }

    [Fact]
    public void Import_flattens_a_traversal_path_in_both_the_file_and_the_metadata_zip_slip()
    {
        var dest = Root("dest");
        var zip = Zip("evil");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            // Metadata carries a path-traversal file name; the WAV is stored under its flattened name.
            using (var w = new StreamWriter(archive.CreateEntry("library.json").Open()))
                w.Write("{\"version\":1,\"categories\":[],\"phrases\":[{\"id\":\"p-x\",\"fileName\":\"../escape.wav\"}]}");
            using (var w = new StreamWriter(archive.CreateEntry("audio/escape.wav").Open()))
                w.Write("evil");
        }

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.True(result.Success);
        // The WAV is contained in audio\ under the phrase's own re-keyed name, and the persisted
        // metadata no longer carries the traversal.
        Assert.True(File.Exists(AdaVoicePaths.AudioPath(dest, "p-x.wav")));
        Assert.Equal("p-x.wav", Assert.Single(new JsonPhraseRepository(dest).Load().Library.Phrases).FileName);
        Assert.False(File.Exists(Path.Combine(dest, "escape.wav"))); // never escaped to the root
        Assert.False(File.Exists(Path.Combine(_work, "escape.wav"))); // nor above it
    }

    // H9 regression: an archive phrase with a NEW id but an existing phrase's file name must not
    // overwrite that phrase's WAV — recordings are irreplaceable (design 04 §3). Import re-keys
    // every incoming WAV to "{id}.wav", so a collision is impossible by construction.
    [Fact]
    public void Merge_never_overwrites_an_existing_phrases_wav_on_a_file_name_collision()
    {
        var dest = Root("dest");
        Seed(dest, "p-local", [9]);

        var zip = Zip("collide");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(archive.CreateEntry("library.json").Open()))
                w.Write("{\"version\":1,\"categories\":[],\"phrases\":[{\"id\":\"p-import\",\"fileName\":\"p-local.wav\"}]}");
            using var audio = archive.CreateEntry("audio/p-local.wav").Open();
            audio.Write([1, 2, 3]);
        }

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(AdaVoicePaths.AudioPath(dest, "p-local.wav"))); // kept intact
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(AdaVoicePaths.AudioPath(dest, "p-import.wav")));
        var imported = new JsonPhraseRepository(dest).Load().Library.Phrases.Single(p => p.Id == "p-import");
        Assert.Equal("p-import.wav", imported.FileName); // metadata matches the re-keyed WAV
    }

    // M9: the ImportResult contract says "Success false means nothing was changed". A failed
    // extract mid-loop (corrupt entry, disk full, blocked path) must fail the whole import —
    // not land some WAVs and then throw out of Import.
    [Fact]
    public void A_failed_extract_mid_loop_fails_the_import_and_changes_nothing()
    {
        var dest = Root("dest");
        Seed(dest, "p-keep", [7]);

        var zip = Zip("blocked");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(archive.CreateEntry("library.json").Open()))
                w.Write("{\"version\":1,\"categories\":[],\"phrases\":[" +
                    "{\"id\":\"p-a\",\"fileName\":\"p-a.wav\"},{\"id\":\"p-b\",\"fileName\":\"p-b.wav\"}]}");
            using (var a = archive.CreateEntry("audio/p-a.wav").Open())
                a.Write([1, 2, 3]);
            using (var b = archive.CreateEntry("audio/p-b.wav").Open())
                b.Write([4, 5, 6]);
        }

        // Block the SECOND phrase's temp path with a directory, so its extract throws after the
        // first WAV was already staged — the classic mid-loop failure.
        Directory.CreateDirectory(AdaVoicePaths.AudioPath(dest, "p-b.wav.importing"));

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        var library = new JsonPhraseRepository(dest).Load().Library;
        Assert.Equal("p-keep", Assert.Single(library.Phrases).Id);              // metadata unchanged
        Assert.False(File.Exists(AdaVoicePaths.AudioPath(dest, "p-a.wav")));    // staged WAV never moved
        Assert.False(File.Exists(AdaVoicePaths.AudioPath(dest, "p-b.wav")));
        Assert.Empty(Directory.GetFiles(AdaVoicePaths.AudioDir(dest), "*.importing")); // temps cleaned
    }

    [Fact]
    public void Duplicate_ids_inside_the_archive_import_only_once()
    {
        var dest = Root("dest");
        var zip = Zip("dupes");
        WriteZipWithLibraryJson(zip, "{\"version\":1,\"categories\":[],\"phrases\":[" +
            "{\"id\":\"p-1\",\"title\":\"first\",\"fileName\":\"a.wav\"}," +
            "{\"id\":\"p-1\",\"title\":\"second\",\"fileName\":\"b.wav\"}]}");

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.True(result.Success);
        var phrase = Assert.Single(new JsonPhraseRepository(dest).Load().Library.Phrases);
        Assert.Equal("first", phrase.Title); // keep-first rule
    }

    [Fact]
    public void A_dangling_category_id_is_remapped_to_the_default()
    {
        var dest = Root("dest");
        var zip = Zip("dangling");
        WriteZipWithLibraryJson(zip,
            "{\"version\":1,\"categories\":[],\"phrases\":[{\"id\":\"p-1\",\"categoryId\":\"c-gone\",\"fileName\":\"p-1.wav\"}]}");

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.True(result.Success);
        var phrase = Assert.Single(new JsonPhraseRepository(dest).Load().Library.Phrases);
        Assert.Equal(Category.DefaultId, phrase.CategoryId); // no phrase may dangle
    }

    // DeleteCategory moves phrases to the default category and the UI protects it by id —
    // a Replace archive from another tool must not be able to remove it.
    [Fact]
    public void Replace_import_always_keeps_the_default_category()
    {
        var dest = Root("dest");
        Seed(dest, "p-old", [1]);
        var zip = Zip("nodefault");
        WriteZipWithLibraryJson(zip,
            "{\"version\":1,\"categories\":[{\"id\":\"c-x\",\"name\":\"Custom\"}]," +
            "\"phrases\":[{\"id\":\"p-1\",\"categoryId\":\"c-x\",\"fileName\":\"p-1.wav\"}]}");

        var result = Archive(dest).Import(zip, ImportMode.Replace);

        Assert.True(result.Success);
        var library = new JsonPhraseRepository(dest).Load().Library;
        Assert.Contains(library.Categories, c => c.Id == Category.DefaultId);
        Assert.Contains(library.Categories, c => c.Id == "c-x");
    }

    [Fact]
    public void Merge_keeps_the_archives_tag_colours_for_new_tag_names()
    {
        var dest = Root("dest");
        Seed(dest, "p-local", [1]);
        var zip = Zip("tags");
        WriteZipWithLibraryJson(zip, "{\"version\":1,\"categories\":[]," +
            "\"phrases\":[{\"id\":\"p-new\",\"fileName\":\"p-new.wav\",\"tags\":[\"warm\"]}]," +
            "\"tags\":[{\"name\":\"warm\",\"color\":\"#FF6B6B\"}]}");

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.True(result.Success);
        var tag = Assert.Single(new JsonPhraseRepository(dest).Load().Library.Tags, t => t.Name == "warm");
        Assert.Equal("#FF6B6B", tag.Color); // the archive's chip colour survives the merge
    }

    // M10: resource caps — a crafted archive must not OOM the app or fill the disk.
    [Fact]
    public void An_oversized_library_json_is_rejected()
    {
        var dest = Root("dest");
        var zip = Zip("huge");
        WriteZipWithLibraryJson(zip,
            "{\"version\":1,\"categories\":[],\"phrases\":[],\"padding\":\"" + new string('x', 17 * 1024 * 1024) + "\"}");

        var result = Archive(dest).Import(zip, ImportMode.Merge);

        Assert.False(result.Success);
        Assert.Contains("large", result.Error!);
    }

    private static void WriteZipWithLibraryJson(string zipPath, string libraryJson)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("library.json").Open());
        writer.Write(libraryJson);
    }

    public void Dispose()
    {
        if (Directory.Exists(_work))
            Directory.Delete(_work, recursive: true);
    }
}
