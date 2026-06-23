using System.Text.Json;
using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// One place for reading and writing the library as JSON, so the normal load and the corrupt-recovery
/// path apply the <b>same</b> validity rule. <see cref="TryParse"/> treats a missing, empty, null, or
/// unparseable document as "no valid library" — this is what keeps a bad backup from re-introducing
/// the silently-empty library that startup validation forbids (design 04 §3).
/// </summary>
internal static class LibraryJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize(Library library) => JsonSerializer.Serialize(library, Options);

    /// <summary>Returns the parsed library, or null if <paramref name="json"/> is missing/empty/
    /// whitespace, the literal <c>null</c>, or not valid JSON.</summary>
    public static Library? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<Library>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
