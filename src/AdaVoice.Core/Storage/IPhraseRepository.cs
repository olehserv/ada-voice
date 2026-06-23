using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// Reads and writes the phrase library. Behind this seam so the JSON store can be swapped for SQLite
/// without touching the services or UI (design 03). <see cref="Load"/> returns a seeded default when
/// nothing is stored yet, and reports a corrupt/unreadable file via the result instead of throwing —
/// so startup never crashes and never silently starts empty (design 04 §3).
/// </summary>
public interface IPhraseRepository
{
    LibraryLoadResult Load();
    void Save(Library library);
}
