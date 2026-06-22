using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// Reads and writes the phrase library. Behind this seam so the JSON store can be swapped for SQLite
/// without touching the services or UI (design 03). <see cref="Load"/> returns a seeded default when
/// nothing is stored yet.
/// </summary>
public interface IPhraseRepository
{
    Library Load();
    void Save(Library library);
}
