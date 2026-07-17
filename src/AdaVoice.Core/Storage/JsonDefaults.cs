using System.Text.Json;

namespace AdaVoice.Core.Storage;

/// <summary>The one <see cref="JsonSerializerOptions"/> shared by every JSON store (library, settings)
/// so they serialize consistently. A neutral home — the settings store shouldn't depend on the
/// library-specific <see cref="LibraryJson"/> class for this.</summary>
internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };
}
