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

    /// <summary>True when the last <see cref="Load"/> found a settings file it could not use and fell
    /// back to defaults. The host surfaces this: a silent reset also drops the wizard mic-calibration
    /// reference, which changes how loud phrases play — the operator must know to re-run calibration
    /// (security scan 2026-07-12 finding 3). A missing or empty file is a clean first run, not this.</summary>
    public bool LoadReplacedCorruptFile { get; private set; }

    public Settings Load()
    {
        LoadReplacedCorruptFile = false;
        var path = AdaVoicePaths.SettingsFile(root);
        if (!File.Exists(path))
            return new Settings();

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return new Settings();

            var settings = JsonSerializer.Deserialize<Settings>(json, Options);
            if (settings is not null)
                return settings;

            LoadReplacedCorruptFile = true; // a real file that held the literal `null`
            return new Settings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Preferences are regenerable — fall back to defaults rather than fail startup, but flag it
            // so the lost calibration is not silent.
            LoadReplacedCorruptFile = true;
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
