using System.Text.Json;
using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// Stores the library as <c>library.json</c> under a data root. Writes go to a temp file and are then
/// atomically moved over the original, so a crash mid-save can never corrupt the library (design 04
/// §3). A missing file loads a seeded default (version 1 + an "Uncategorized" category).
/// </summary>
public sealed class JsonPhraseRepository(string root) : IPhraseRepository
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public Library Load()
    {
        var path = AdaVoicePaths.LibraryFile(root);
        if (!File.Exists(path))
            return Default();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Library>(json, Options) ?? Default();
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
