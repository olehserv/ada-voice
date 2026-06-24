using System.Text.Json;
using AdaVoice.Core.Domain;

namespace AdaVoice.Core.Storage;

/// <summary>
/// Stores user preferences as <c>settings.json</c> under the data root, with the same atomic write as
/// the library (temp file then rename, so a crash mid-save can't corrupt it).
/// </summary>
/// <remarks>
/// Unlike the library, settings are regenerable preferences — not irreplaceable user data. So a
/// missing <i>or</i> unreadable/corrupt file simply loads defaults: no quarantine, no backup-recovery,
/// no load-status type. The deliberate asymmetry keeps this store simple.
/// </remarks>
public sealed class JsonSettingsRepository(string root)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public Settings Load()
    {
        var path = AdaVoicePaths.SettingsFile(root);
        if (!File.Exists(path))
            return new Settings();

        try
        {
            var json = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(json)
                ? new Settings()
                : JsonSerializer.Deserialize<Settings>(json, Options) ?? new Settings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Preferences are regenerable — fall back to defaults rather than fail startup.
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        Directory.CreateDirectory(root);

        var path = AdaVoicePaths.SettingsFile(root);
        var tmp = path + ".tmp";
        try
        {
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
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
            // Best-effort cleanup; the original failure is what matters.
        }
    }
}
