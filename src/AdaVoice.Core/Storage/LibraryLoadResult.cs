using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>How loading the library went. Lets <see cref="IPhraseRepository.Load"/> report a problem
/// without throwing, so the app can surface a message and never start silently empty (design 04 §3).</summary>
public enum LibraryLoadStatus
{
    /// <summary>Parsed an existing file.</summary>
    Loaded,

    /// <summary>No file yet — normal first run; a seeded default was returned.</summary>
    SeededDefault,

    /// <summary>The file existed but could not be parsed. It was quarantined and a seeded default
    /// returned — the app must surface this, not start silently empty.</summary>
    Corrupt,

    /// <summary>The file existed but could not be read (e.g. a transient lock). A seeded default was
    /// returned and the file was left untouched (not quarantined).</summary>
    ReadError,
}

/// <summary>The outcome of a load: the library plus how it was obtained.</summary>
public sealed record LibraryLoadResult(Library Library, LibraryLoadStatus Status, string? Detail = null);
