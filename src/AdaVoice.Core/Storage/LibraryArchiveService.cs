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

    // Resource caps: a crafted or corrupted archive must not be able to fill the disk or OOM
    // the app. Realistic takes are seconds long (~4 MB as 16-bit WAV); a 45-minute take is
    // ~256 MB — the caps leave generous headroom over anything the recorder produces.
    private const int MaxEntries = 10_000;
    internal const long MaxLibraryJsonBytes = 16 * 1024 * 1024;
    private const long MaxWavBytes = 256 * 1024 * 1024;
    private const long MaxTotalAudioBytes = 1024L * 1024 * 1024;

    /// <summary>Write a zip of the current library's metadata + active phrase WAVs to
    /// <paramref name="destinationZipPath"/>. Built into a temp file then moved into place so a
    /// partial zip is never left at the destination. Phrase versions are not carried by an export
    /// (v1 limitation — see <see cref="CreateArchive"/>); returns how many were dropped so the caller
    /// can tell the operator, rather than losing them silently.</summary>
    public int Export(string destinationZipPath)
    {
        var library = repository.Load().Library;

        Directory.CreateDirectory(Path.GetDirectoryName(destinationZipPath)!);
        var tmp = destinationZipPath + ".tmp";
        try
        {
            var droppedVersions = CreateArchive(tmp, library);
            File.Move(tmp, destinationZipPath, overwrite: true);
            return droppedVersions;
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
        if (zip.Entries.Count > MaxEntries)
            return new ImportResult(false, 0, 0, $"the archive has too many entries ({zip.Entries.Count})");

        var entry = zip.GetEntry("library.json");
        if (entry is not null && entry.Length > MaxLibraryJsonBytes)
            return new ImportResult(false, 0, 0, "the archive's library.json is unreasonably large");
        var imported = entry is null ? null : LibraryJson.TryParse(ReadEntry(entry));
        if (imported is null)
            return new ImportResult(false, 0, 0, "the archive has no valid library.json");
        if (imported.Version != SupportedVersion)
            return new ImportResult(false, 0, 0, $"unsupported library version {imported.Version} (expected {SupportedVersion})");

        // Normalize the catalogue before anything else: drop blank-id phrases and duplicate ids
        // (keep the first) so the re-key and merge maths below are well-defined. Also flatten
        // every file name, so a crafted entry can never put a path-traversal value into the WAV
        // lookup — the zip entry is looked up by this bare name. Versions are stripped defensively:
        // an export never carries version audio (see CreateArchive), so a hand-crafted or
        // third-party archive claiming versions would otherwise catalogue references to files that
        // were never staged.
        imported = imported with
        {
            Phrases = imported.Phrases
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .DistinctBy(p => p.Id)
                .Select(p => p with { FileName = Path.GetFileName(p.FileName), Versions = [] })
                .ToList(),
        };

        // Re-key every imported WAV to the phrase's own "{id}.wav" (the same convention Add uses).
        // An archive-supplied file name may collide with a WAV that belongs to a DIFFERENT existing
        // phrase — extraction would silently overwrite an irreplaceable recording. Keyed to the id,
        // a new-id phrase can never target a kept phrase's file. The archive name survives only as
        // the lookup key for extraction. Path.GetFileName also guards against a crafted id.
        var archiveNames = imported.Phrases.ToDictionary(p => p.Id, p => p.FileName);
        imported = imported with
        {
            Phrases = imported.Phrases.Select(p => p with { FileName = Path.GetFileName($"{p.Id}.wav") }).ToList(),
        };

        // The seeded default category must always exist (DeleteCategory and the UI rely on it) —
        // a Replace archive written by another tool may lack it.
        if (mode == ImportMode.Replace && imported.Categories.All(c => c.Id != Category.DefaultId))
            imported.Categories.Insert(0,
                new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080", SortOrder = 0 });

        var current = repository.Load().Library;

        // No phrase may dangle: remap a CategoryId the merged result won't contain to the default.
        var knownCategoryIds = imported.Categories.Select(c => c.Id).ToHashSet();
        if (mode == ImportMode.Merge)
            knownCategoryIds.UnionWith(current.Categories.Select(c => c.Id));
        imported = imported with
        {
            Phrases = imported.Phrases
                .Select(p => knownCategoryIds.Contains(p.CategoryId) ? p : p with { CategoryId = Category.DefaultId })
                .ToList(),
        };

        var (result, added) = mode == ImportMode.Replace ? Replace(imported) : Merge(current, imported);

        // WAV-first AND transactional: stage every WAV under a temp name, move them into place
        // only once all extracted cleanly, then save the metadata. A corrupt entry or a full disk
        // mid-import therefore keeps the promise of the ImportResult contract — nothing changed
        // (at worst, already-moved WAVs the unsaved metadata never references: orphans, and the
        // {id}.wav keying means they can never have overwritten a kept phrase's file).
        var staged = new List<(string Tmp, string Final)>();
        try
        {
            long totalAudioBytes = 0;
            foreach (var phrase in added)
                StageAudio(zip, archiveNames[phrase.Id], phrase.FileName, staged, ref totalAudioBytes);

            foreach (var (tmp, final) in staged)
                File.Move(tmp, final, overwrite: true);

            repository.Save(result);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            foreach (var (tmp, _) in staged)
                TryDelete(tmp);
            return new ImportResult(false, 0, 0, $"import failed, the library was not changed: {ex.Message}");
        }

        return new ImportResult(true, added.Count, imported.Phrases.Count - added.Count);
    }

    private static (Library result, List<PhraseEntry> added) Replace(Library imported) =>
        (imported, imported.Phrases);

    private static (Library result, List<PhraseEntry> added) Merge(Library current, Library imported)
    {
        var existingPhraseIds = current.Phrases.Select(p => p.Id).ToHashSet();
        var existingCategoryIds = current.Categories.Select(c => c.Id).ToHashSet();
        var existingTagNames = current.Tags.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Note: imported phrases keep their own SortOrder, which can collide with the current
        // library's values — the WPF phase should re-number on merge. Fine for now.
        var added = imported.Phrases.Where(p => !existingPhraseIds.Contains(p.Id)).ToList();

        // Build a fresh record instead of mutating the loaded instance (a trap for any future
        // caching repository). The archive's tag registry merges in too, keeping its colours
        // for names we don't have yet — merged-in phrases keep their chip colours.
        var result = current with
        {
            Phrases = [.. current.Phrases, .. added],
            Categories = [.. current.Categories, .. imported.Categories.Where(c => !existingCategoryIds.Contains(c.Id))],
            Tags = [.. current.Tags, .. imported.Tags.Where(t => !existingTagNames.Contains(t.Name))],
        };
        return (result, added);
    }

    /// <summary>Build the zip and return how many version recordings were dropped from it. Only the
    /// primary WAV is ever zipped (v1 limitation, not yet worth the re-keying/staging work a version
    /// archive would need); the embedded <c>library.json</c>'s <c>Versions</c> lists are stripped too,
    /// so an import can never see a version it has no audio for.</summary>
    private int CreateArchive(string zipPath, Library library)
    {
        var droppedVersions = library.Phrases.Sum(p => p.Versions.Count);
        var stripped = library with
        {
            Phrases = library.Phrases.Select(p => p with { Versions = [] }).ToList(),
        };

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        var entry = zip.CreateEntry("library.json");
        using (var writer = new StreamWriter(entry.Open()))
            writer.Write(LibraryJson.Serialize(stripped)); // re-serialize, never copy a stale/corrupt file

        foreach (var phrase in stripped.Phrases) // active phrases only — orphans aren't referenced
        {
            var path = AdaVoicePaths.AudioPath(root, phrase.FileName);
            if (File.Exists(path)) // a broken phrase (missing WAV) is skipped, not fatal
                zip.CreateEntryFromFile(path, "audio/" + phrase.FileName);
        }

        return droppedVersions;
    }

    /// <summary>Extract one WAV to a <c>.importing</c> temp file next to its final name and record
    /// the pair — Import moves the batch into place only after every entry extracted cleanly.</summary>
    private void StageAudio(ZipArchive zip, string archiveFileName, string destFileName,
        List<(string Tmp, string Final)> staged, ref long totalAudioBytes)
    {
        var source = zip.GetEntry("audio/" + archiveFileName);
        if (source is null)
            return; // archive without this WAV (e.g. a broken phrase) — metadata still imports

        // Header-declared sizes; ExtractToFile verifies them against the actual stream, so a
        // lying header fails the extract (and the transaction) rather than bypassing the cap.
        if (source.Length > MaxWavBytes)
            throw new InvalidDataException($"audio entry '{archiveFileName}' is unreasonably large");
        totalAudioBytes += source.Length;
        if (totalAudioBytes > MaxTotalAudioBytes)
            throw new InvalidDataException("the archive's total audio exceeds the import limit");

        Directory.CreateDirectory(AdaVoicePaths.AudioDir(root));
        // Zip-slip guard: flatten to a bare file name so a crafted entry can never escape audio\.
        // Overwrite is safe here: dest is "{id}.wav" and the id is new to this library (Merge adds
        // only new ids; Replace swaps the whole catalogue), so it never clobbers a kept phrase.
        var final = AdaVoicePaths.AudioPath(root, Path.GetFileName(destFileName));
        var tmp = final + ".importing";
        source.ExtractToFile(tmp, overwrite: true);
        staged.Add((tmp, final));
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
