using System.IO.Compression;
using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// Daily local backups of the data root (design 04 §4): one zip per day under <c>backups\</c>
/// containing <c>library.json</c>, <c>settings.json</c> (if present), and the whole <c>audio\</c>
/// folder — the audio is the irreplaceable data, so orphaned (<c>deleted-…</c>) takes are included.
/// The newest <see cref="_keep"/> backups are kept. Backups are best-effort: a failure never throws to
/// the caller, so it can run on the startup path without risking the app.
/// </summary>
/// <remarks>The zip is built synchronously; with tens of MB of audio that briefly blocks the caller.
/// Fine for the console host; the WPF phase should run it off the UI thread.</remarks>
public sealed class BackupService(string root, int keep = 7)
{
    private readonly int _keep = keep;

    /// <summary>Create <paramref name="today"/>'s backup if it does not exist yet, then prune to the
    /// newest <see cref="_keep"/>. Returns the path created, or null if today's backup already existed
    /// or the backup failed. Never throws.</summary>
    public string? EnsureDailyBackup(DateOnly today)
    {
        var finalPath = AdaVoicePaths.BackupFile(root, today);
        if (File.Exists(finalPath))
            return null; // already backed up today

        // Build into a temp name that does NOT end in .zip, so a half-written or orphaned temp can
        // never be picked up by pruning or recovery (which only look at *.zip).
        var tmp = finalPath + ".tmp";
        try
        {
            Directory.CreateDirectory(AdaVoicePaths.BackupsDir(root));
            CreateZip(tmp);
            File.Move(tmp, finalPath, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            return null; // best-effort: a failed backup must not break startup
        }

        Prune();
        return finalPath;
    }

    /// <summary>For corrupt-library recovery: return the library from the newest backup whose
    /// <c>library.json</c> is valid, trying older backups if the newest is itself unusable. Returns
    /// null if no backup yields a valid library. Never throws.</summary>
    public Library? TryReadLatestLibrary()
    {
        foreach (var path in BackupFilesNewestFirst())
        {
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.GetEntry("library.json");
                if (entry is null)
                    continue;
                if (entry.Length > LibraryArchiveService.MaxLibraryJsonBytes)
                    continue; // absurd size = not a backup we wrote; never read it into memory

                using var reader = new StreamReader(entry.Open());
                var library = LibraryJson.TryParse(reader.ReadToEnd());
                if (library is not null)
                    return library; // applies the same validity rule as a normal load
            }
            catch
            {
                // Unreadable or corrupt zip — try the next-newest backup.
            }
        }

        return null;
    }

    /// <summary>The date of the newest backup, or null if none exist yet. Used by the Settings
    /// window's backup status readout.</summary>
    public DateOnly? LatestBackupDate()
    {
        var newest = BackupFilesNewestFirst().FirstOrDefault();
        if (newest is null)
            return null;

        var name = Path.GetFileNameWithoutExtension(newest);
        var dateText = name[AdaVoicePaths.BackupFilePrefix.Length..];
        return DateOnly.TryParse(dateText, out var date) ? date : null;
    }

    private void CreateZip(string zipPath)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddIfExists(zip, AdaVoicePaths.LibraryFile(root), "library.json");
        AddIfExists(zip, AdaVoicePaths.SettingsFile(root), "settings.json");

        var audioDir = AdaVoicePaths.AudioDir(root);
        if (Directory.Exists(audioDir))
            foreach (var file in Directory.EnumerateFiles(audioDir))
                zip.CreateEntryFromFile(file, "audio/" + Path.GetFileName(file));
    }

    private static void AddIfExists(ZipArchive zip, string path, string entryName)
    {
        if (File.Exists(path))
            zip.CreateEntryFromFile(path, entryName);
    }

    private void Prune()
    {
        foreach (var old in BackupFilesNewestFirst().Skip(_keep))
            TryDelete(old);
    }

    /// <summary>Completed backups, newest first. Filtered by prefix + <c>.zip</c> in code (not a glob)
    /// to avoid Windows wildcard quirks and to ignore any leftover temp file.</summary>
    private List<string> BackupFilesNewestFirst()
    {
        var dir = AdaVoicePaths.BackupsDir(root);
        if (!Directory.Exists(dir))
            return [];

        return Directory.EnumerateFiles(dir)
            .Where(IsBackupFile)
            .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsBackupFile(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith(AdaVoicePaths.BackupFilePrefix, StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
