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
        // The WAV is contained in audio\, and the persisted metadata no longer carries the traversal.
        Assert.True(File.Exists(AdaVoicePaths.AudioPath(dest, "escape.wav")));
        Assert.Equal("escape.wav", Assert.Single(new JsonPhraseRepository(dest).Load().Library.Phrases).FileName);
        Assert.False(File.Exists(Path.Combine(dest, "escape.wav"))); // never escaped to the root
        Assert.False(File.Exists(Path.Combine(_work, "escape.wav"))); // nor above it
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
