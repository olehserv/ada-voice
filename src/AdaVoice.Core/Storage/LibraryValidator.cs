using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// Checks the library against the file system at startup (design 04 §3): a phrase whose WAV is missing
/// is reported as <i>broken</i> so the UI can flag it, instead of crashing playback later. The
/// file-existence check is injected, so this stays pure and unit-testable without touching disk.
/// Broken state is intentionally runtime-only — it is never written back into <c>library.json</c>.
/// </summary>
public static class LibraryValidator
{
    /// <summary>Returns the ids of phrases whose audio file does not exist, in library order.</summary>
    public static IReadOnlyList<string> FindBrokenPhraseIds(Library library, Func<string, bool> audioExists) =>
        library.Phrases.Where(p => !audioExists(p.FileName)).Select(p => p.Id).ToList();

    /// <summary>Returns the ids of phrase <i>versions</i> whose audio file does not exist. Kept separate
    /// from <see cref="FindBrokenPhraseIds"/> on purpose: a missing version must not flag the whole
    /// phrase broken (its primary still plays) — the Versions window flags the one bad tile instead
    /// (security scan 2026-07-12 finding 5).</summary>
    public static IReadOnlyList<string> FindBrokenVersionIds(Library library, Func<string, bool> audioExists) =>
        library.Phrases.SelectMany(p => p.Versions).Where(v => !audioExists(v.FileName)).Select(v => v.Id).ToList();
}
