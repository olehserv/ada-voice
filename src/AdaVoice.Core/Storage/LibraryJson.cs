using System.Text.Json;
using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// One place for reading and writing the library as JSON, so the normal load and the corrupt-recovery
/// path apply the <b>same</b> validity rule. <see cref="TryParse"/> treats a missing, empty, null, or
/// unparseable document as "no valid library" — this is what keeps a bad backup from re-introducing
/// the silently-empty library that startup validation forbids (design 04 §3). It also normalizes every
/// phrase/version <c>FileName</c> to a bare file name, so a tampered document cannot smuggle a
/// path-traversal or absolute path into the file operations downstream.
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
            return Sanitize(JsonSerializer.Deserialize<Library>(json, Options));
        }
        catch (Exception ex) when (ex is JsonException or NullReferenceException)
        {
            // Malformed JSON, or a null in a spot the shape forbids (e.g. a null array element that
            // System.Text.Json produces from `"phrases": [null]`). Either way it is not a usable
            // library — return null so Load quarantines and tries backup recovery, instead of letting
            // the exception crash startup (security scan 2026-07-12 finding 2).
            return null;
        }
    }

    // Every FileName is a bare "{id}.wav" produced by the app. A tampered library.json could carry a
    // path-traversal ("..\..\secret") or absolute path; flatten it here — the one parse choke point for
    // load, backup recovery, and import — so no downstream file op (play/preview/export/delete) can
    // escape the audio\ directory. Well-formed names are unchanged.
    private static Library? Sanitize(Library? library)
    {
        // A null phrase list means "no valid library": treat it as invalid so it is quarantined, never
        // coalesced to empty — that would be the silently-empty library design 04 §3 forbids. The other
        // collections are additive (an older/partial file legitimately has none), so a malformed null
        // there is normalized to empty rather than failing the whole load (security scan finding 2).
        if (library is null || library.Phrases is null)
            return null;

        library = library with
        {
            Categories = library.Categories ?? [],
            Tags = library.Tags ?? [],
            Conversations = library.Conversations ?? [],
        };

        for (var i = 0; i < library.Phrases.Count; i++)
        {
            var phrase = library.Phrases[i];
            library.Phrases[i] = phrase with
            {
                FileName = Path.GetFileName(phrase.FileName ?? ""),
                Tags = phrase.Tags ?? [],
                Versions = (phrase.Versions ?? [])
                    .Select(v => v with { FileName = Path.GetFileName(v.FileName ?? "") })
                    .ToList(),
            };
        }

        return library;
    }
}
