using System.Text.Json;
using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// Stores the library as <c>library.json</c> under a data root. Writes go to a temp file and are then
/// atomically moved over the original, so a crash mid-save can never corrupt the library (design 04
/// §3). A missing file loads a seeded default (version 1 + an "Uncategorized" category). A file that
/// cannot be parsed is quarantined (renamed, never destroyed) and a seeded default returned — startup
/// never crashes and never silently starts empty.
/// </summary>
public sealed class JsonPhraseRepository(string root) : IPhraseRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

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

        Library? library;
        try
        {
            // Empty/zero-length or whitespace-only (a crash mid-write) is treated as corrupt, not as
            // an empty library.
            library = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<Library>(json, Options);
        }
        catch (JsonException ex)
        {
            return Quarantine(path, ex.Message);
        }

        // Valid JSON of the literal `null` also deserializes to null — would otherwise start silently
        // empty, which design 04 §3 forbids.
        if (library is null)
            return Quarantine(path, "library.json was empty or contained null");

        return new LibraryLoadResult(library, LibraryLoadStatus.Loaded);
    }

    private LibraryLoadResult Quarantine(string path, string detail)
    {
        // Preserve the bad file so the operator's data is never destroyed; the stamp keeps repeated
        // failures from overwriting each other.
        // TODO(BackupService): before falling back to the seeded default, try recovering the newest
        // daily backup from backups\ (the writer for that format lands in a later storage slice).
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        try
        {
            File.Move(path, AdaVoicePaths.CorruptQuarantineFile(root, stamp), overwrite: false);
        }
        catch
        {
            // Best-effort: if it cannot be moved (e.g. now locked), leave it in place rather than crash.
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
            File.WriteAllText(tmp, JsonSerializer.Serialize(library, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static Library Default() => new()
    {
        Version = 1,
        Categories = [new Category { Id = "c-default", Name = "Uncategorized", Color = "#808080", SortOrder = 0 }],
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
