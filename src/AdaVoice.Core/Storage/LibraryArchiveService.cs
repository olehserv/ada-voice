using System.IO.Compression;
using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>How an import combines the archive with the current library.</summary>
public enum ImportMode
{
    /// <summary>Keep the current library; add only categories/phrases whose id is not already present
    /// (an id clash keeps the existing entry).</summary>
    Merge,

    /// <summary>Replace the current library with the archive's. Existing WAVs are never deleted — any
    /// no-longer-referenced file simply becomes an unused orphan on disk.</summary>
    Replace,
}

/// <summary>The outcome of an import. <see cref="Success"/> false means nothing was changed.</summary>
public sealed record ImportResult(bool Success, int Added, int Skipped, string? Error = null);

/// <summary>
/// Manual export and import of the library (design 04 §4). The archive is a plain zip with the same
/// layout as a daily backup — <c>library.json</c> at the root plus <c>audio/{fileName}</c> — so an
/// import can also restore from a daily backup, not just an export. Export contains only the active
/// phrases (orphaned <c>deleted-…</c> takes are excluded); import validates the schema version and
/// either merges or replaces. Settings are machine-specific and not part of the archive.
/// </summary>
public sealed class LibraryArchiveService(string root, IPhraseRepository repository)
{
    private const int SupportedVersion = 1;

    /// <summary>Write a zip of the current library's metadata + active phrase WAVs to
    /// <paramref name="destinationZipPath"/>. Built into a temp file then moved into place so a
    /// partial zip is never left at the destination.</summary>
    public void Export(string destinationZipPath)
    {
        var library = repository.Load().Library;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationZipPath)!);
        var tmp = destinationZipPath + ".tmp";
        try
        {
            CreateArchive(tmp, library);
            File.Move(tmp, destinationZipPath, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    /// <summary>Read <paramref name="sourceZipPath"/> and merge or replace the current library with it.
    /// Returns a failure (changing nothing) if the archive's <c>library.json</c> is missing/invalid or
    /// its schema version is unsupported.</summary>
    public ImportResult Import(string sourceZipPath, ImportMode mode)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(sourceZipPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new ImportResult(false, 0, 0, $"could not open archive: {ex.Message}");
        }

        using var _ = zip;
        var entry = zip.GetEntry("library.json");
        var imported = entry is null ? null : LibraryJson.TryParse(ReadEntry(entry));
        if (imported is null)
            return new ImportResult(false, 0, 0, "the archive has no valid library.json");
        if (imported.Version != SupportedVersion)
            return new ImportResult(false, 0, 0, $"unsupported library version {imported.Version} (expected {SupportedVersion})");

        // Flatten every file name at ingest, so a crafted entry can never put a path-traversal value
        // into the WAV lookup — the zip entry is looked up by this bare name.
        imported = imported with
        {
            Phrases = imported.Phrases.Select(p => p with { FileName = Path.GetFileName(p.FileName) }).ToList(),
        };

        // Re-key every imported WAV to the phrase's own "{id}.wav" (the same convention Add uses).
        // An archive-supplied file name may collide with a WAV that belongs to a DIFFERENT existing
        // phrase — extraction would silently overwrite an irreplaceable recording. Keyed to the id,
        // a new-id phrase can never target a kept phrase's file. The archive name survives only as
        // the lookup key for extraction. Path.GetFileName also guards against a crafted id.
        var archiveNames = imported.Phrases
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First().FileName);
        imported = imported with
        {
            Phrases = imported.Phrases.Select(p => p with { FileName = Path.GetFileName($"{p.Id}.wav") }).ToList(),
        };

        var current = repository.Load().Library;
        var (result, added) = mode == ImportMode.Replace ? Replace(imported) : Merge(current, imported);

        // WAV-first: land the audio before committing metadata, so a failed extract catalogues nothing
        // (the same ordering rule as PhraseLibraryService.Add).
        foreach (var phrase in added)
            ExtractAudio(zip, archiveNames[phrase.Id], phrase.FileName);

        repository.Save(result);
        return new ImportResult(true, added.Count, imported.Phrases.Count - added.Count);
    }

    private static (Library result, List<PhraseEntry> added) Replace(Library imported) =>
        (imported, imported.Phrases);

    private static (Library result, List<PhraseEntry> added) Merge(Library current, Library imported)
    {
        var existingPhraseIds = current.Phrases.Select(p => p.Id).ToHashSet();
        var existingCategoryIds = current.Categories.Select(c => c.Id).ToHashSet();

        // Note: imported phrases keep their own SortOrder, which can collide with the current
        // library's values — the WPF phase should re-number on merge. Fine for now.
        var added = imported.Phrases.Where(p => !existingPhraseIds.Contains(p.Id)).ToList();
        current.Phrases.AddRange(added);
        current.Categories.AddRange(imported.Categories.Where(c => !existingCategoryIds.Contains(c.Id)));
        return (current, added);
    }

    private void CreateArchive(string zipPath, Library library)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var entry = zip.CreateEntry("library.json");
        using (var writer = new StreamWriter(entry.Open()))
            writer.Write(LibraryJson.Serialize(library)); // re-serialize, never copy a stale/corrupt file

        foreach (var phrase in library.Phrases) // active phrases only — orphans aren't referenced
        {
            var path = AdaVoicePaths.AudioPath(root, phrase.FileName);
            if (File.Exists(path)) // a broken phrase (missing WAV) is skipped, not fatal
                zip.CreateEntryFromFile(path, "audio/" + phrase.FileName);
        }
    }

    private void ExtractAudio(ZipArchive zip, string archiveFileName, string destFileName)
    {
        var source = zip.GetEntry("audio/" + archiveFileName);
        if (source is null)
            return; // archive without this WAV (e.g. a broken phrase) — metadata still imports

        Directory.CreateDirectory(AdaVoicePaths.AudioDir(root));
        // Zip-slip guard: flatten to a bare file name so a crafted entry can never escape audio\.
        // Overwrite is safe here: dest is "{id}.wav" and the id is new to this library (Merge adds
        // only new ids; Replace swaps the whole catalogue), so it never clobbers a kept phrase.
        var dest = AdaVoicePaths.AudioPath(root, Path.GetFileName(destFileName));
        source.ExtractToFile(dest, overwrite: true);
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
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
