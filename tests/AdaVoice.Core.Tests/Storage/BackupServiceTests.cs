using System.IO.Compression;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests.Storage;

public class BackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-bk-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EnsureDailyBackup_creates_a_zip_with_library_and_audio()
    {
        SeedLibrary("p-1");
        WriteAudio("p-1.wav");

        var path = new BackupService(_root).EnsureDailyBackup(new DateOnly(2026, 6, 23));

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
        using var zip = ZipFile.OpenRead(path!);
        Assert.NotNull(zip.GetEntry("library.json"));
        Assert.NotNull(zip.GetEntry("audio/p-1.wav"));
    }

    [Fact]
    public void EnsureDailyBackup_is_a_noop_the_second_time_same_day()
    {
        SeedLibrary("p-1");
        var service = new BackupService(_root);
        var day = new DateOnly(2026, 6, 23);

        Assert.NotNull(service.EnsureDailyBackup(day));
        Assert.Null(service.EnsureDailyBackup(day)); // already backed up today
    }

    [Fact]
    public void EnsureDailyBackup_keeps_only_the_newest_N()
    {
        SeedLibrary("p-1");
        var service = new BackupService(_root, keep: 3);
        foreach (var day in new[] { 1, 2, 3, 4, 5 })
            service.EnsureDailyBackup(new DateOnly(2026, 6, day));

        var kept = Directory.GetFiles(AdaVoicePaths.BackupsDir(_root))
            .Select(f => Path.GetFileName(f)!)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(
            ["adavoice-backup-2026-06-03.zip", "adavoice-backup-2026-06-04.zip", "adavoice-backup-2026-06-05.zip"],
            kept);
    }

    [Fact]
    public void TryReadLatestLibrary_returns_the_library_from_the_newest_backup()
    {
        var service = new BackupService(_root);
        SeedLibrary("p-old");
        service.EnsureDailyBackup(new DateOnly(2026, 6, 1));
        SeedLibrary("p-new");
        service.EnsureDailyBackup(new DateOnly(2026, 6, 2));

        var library = service.TryReadLatestLibrary();

        Assert.Equal("p-new", Assert.Single(library!.Phrases).Id);
    }

    [Fact]
    public void TryReadLatestLibrary_skips_an_invalid_backup_and_uses_an_older_good_one()
    {
        SeedLibrary("p-good");
        new BackupService(_root).EnsureDailyBackup(new DateOnly(2026, 6, 1));
        // A newer backup whose library.json is the literal null must be skipped, not start empty.
        WriteBackupWithLibraryJson(new DateOnly(2026, 6, 2), "null");

        var library = new BackupService(_root).TryReadLatestLibrary();

        Assert.Equal("p-good", Assert.Single(library!.Phrases).Id);
    }

    [Fact]
    public void TryReadLatestLibrary_returns_null_when_no_backups_exist()
    {
        Assert.Null(new BackupService(_root).TryReadLatestLibrary());
    }

    [Fact]
    public void LatestBackupDate_returns_the_newest_backups_date()
    {
        SeedLibrary("p-1");
        var service = new BackupService(_root);
        service.EnsureDailyBackup(new DateOnly(2026, 6, 1));
        service.EnsureDailyBackup(new DateOnly(2026, 6, 3));

        Assert.Equal(new DateOnly(2026, 6, 3), service.LatestBackupDate());
    }

    [Fact]
    public void LatestBackupDate_returns_null_when_no_backups_exist()
    {
        Assert.Null(new BackupService(_root).LatestBackupDate());
    }

    [Fact]
    public void Corrupt_library_is_recovered_through_the_real_wired_path()
    {
        // The whole point of the slice: a real backup + a corrupt file + the wiring the host uses.
        SeedLibrary("p-x");
        var backup = new BackupService(_root);
        backup.EnsureDailyBackup(new DateOnly(2026, 6, 1));
        File.WriteAllText(AdaVoicePaths.LibraryFile(_root), "{ broken");

        var result = new JsonPhraseRepository(_root, backup.TryReadLatestLibrary).Load();

        Assert.Equal(LibraryLoadStatus.RecoveredFromBackup, result.Status);
        Assert.Equal("p-x", Assert.Single(result.Library.Phrases).Id);
    }

    private void SeedLibrary(string phraseId)
    {
        var repo = new JsonPhraseRepository(_root);
        var library = repo.Load().Library;
        library.Phrases.Clear();
        library.Phrases.Add(new PhraseEntry { Id = phraseId, FileName = phraseId + ".wav" });
        repo.Save(library);
    }

    private void WriteAudio(string fileName)
    {
        Directory.CreateDirectory(AdaVoicePaths.AudioDir(_root));
        File.WriteAllBytes(AdaVoicePaths.AudioPath(_root, fileName), [0, 1, 2, 3]);
    }

    private void WriteBackupWithLibraryJson(DateOnly date, string libraryJson)
    {
        Directory.CreateDirectory(AdaVoicePaths.BackupsDir(_root));
        using var zip = ZipFile.Open(AdaVoicePaths.BackupFile(_root, date), ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("library.json").Open());
        writer.Write(libraryJson);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
