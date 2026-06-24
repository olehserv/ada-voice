using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// Stores the library as <c>library.json</c> under a data root. Writes go to a temp file and are then
/// atomically moved over the original, so a crash mid-save can never corrupt the library (design 04
/// §3). A missing file loads a seeded default (version 1 + an "Uncategorized" category). A file that
/// cannot be parsed is quarantined (renamed, never destroyed); if <paramref name="recoverFromBackup"/>
/// yields a valid library it is restored, otherwise a seeded default is returned — startup never
/// crashes and never silently starts empty.
/// </summary>
/// <param name="recoverFromBackup">Optional: called when <c>library.json</c> is corrupt to fetch a
/// library from the newest good backup. Null disables recovery (corrupt → seeded default).</param>
public sealed class JsonPhraseRepository(string root, Func<Library?>? recoverFromBackup = null) : IPhraseRepository
{
    public LibraryLoadResult Load()
    {
        var path = AdaVoicePaths.LibraryFile(root);
        if (!File.Exists(path))
            return new LibraryLoadResult(Default(), LibraryLoadStatus.SeededDefault);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Transient (a lock from an AV scanner or backup-in-progress), not corruption. The file is
            // probably fine, so do NOT quarantine — renaming a good-but-locked file would lose it.
            return new LibraryLoadResult(Default(), LibraryLoadStatus.ReadError, ex.Message);
        }

        // TryParse treats empty/whitespace (a crash mid-write), the literal `null`, and malformed JSON
        // all as "no valid library" — none of these may become a silently-empty library (design 04 §3).
        var library = LibraryJson.TryParse(json);
        if (library is null)
            return Quarantine(path, "library.json was empty, null, or could not be parsed");

        return new LibraryLoadResult(library, LibraryLoadStatus.Loaded);
    }

    private LibraryLoadResult Quarantine(string path, string detail)
    {
        // Preserve the bad file so the operator's data is never destroyed; the stamp keeps repeated
        // failures from overwriting each other.
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        try
        {
            File.Move(path, AdaVoicePaths.CorruptQuarantineFile(root, stamp), overwrite: false);
        }
        catch
        {
            // Best-effort: if it cannot be moved (e.g. now locked), leave it in place rather than crash.
        }

        // Try the newest good backup before falling back to an empty library.
        var recovered = recoverFromBackup?.Invoke();
        if (recovered is not null)
        {
            TrySave(recovered); // restore library.json so the recovery survives a restart (best-effort)
            return new LibraryLoadResult(recovered, LibraryLoadStatus.RecoveredFromBackup, detail);
        }

        return new LibraryLoadResult(Default(), LibraryLoadStatus.Corrupt, detail);
    }

    public void Save(Library library)
    {
        Directory.CreateDirectory(root);

        var path = AdaVoicePaths.LibraryFile(root);
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, LibraryJson.Serialize(library));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private void TrySave(Library library)
    {
        try
        {
            Save(library);
        }
        catch
        {
            // Disk is broken; keep the recovered library in memory for this session. A restart will
            // degrade to a seeded default — the best we can do without a working disk.
        }
    }

    private static Library Default() => new()
    {
        Version = 1,
        Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080", SortOrder = 0 }],
        Phrases = [],
    };

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; the original failure is what matters.
        }
    }
}
