using AdaVoice.Core.Storage;

namespace AdaVoice.Host;

/// <summary>
/// The slice of settings the Board UI can change. Kept behind a seam (like <see cref="IPlaybackHost"/>)
/// so the settings view-model is unit-testable with a fake. <see cref="EngineHost"/> implements it.
/// </summary>
/// <remarks>
/// Apply and save are split on purpose: a slider drag fires many changes, so <see cref="SetMicDuckDb"/>
/// only updates the live engine + in-memory settings (cheap), and <see cref="SaveSettings"/> writes the
/// file once when the drag ends.
/// </remarks>
public interface ISettingsHost
{
    /// <summary>The current mic-duck level in dB (negative = quieter; 0 = no duck).</summary>
    double MicDuckDb { get; }

    /// <summary>Set the duck level: apply it to the running engine and remember it in memory. Does not
    /// write to disk — call <see cref="SaveSettings"/> to persist.</summary>
    void SetMicDuckDb(double db);

    /// <summary>Persist the current settings to disk (call when a slider drag finishes).</summary>
    void SaveSettings();

    /// <summary>The window's saved size and position, or null if it has never been saved (use defaults).</summary>
    WindowPlacement? WindowPlacement { get; }

    /// <summary>Remember the window's size and position and persist it (called when the window closes).</summary>
    void SaveWindowPlacement(double width, double height, double left, double top);

    /// <summary>True once the setup wizard has been completed at least once.</summary>
    bool WizardCompleted { get; }

    /// <summary>Mark the setup wizard completed and persist immediately.</summary>
    void MarkWizardCompleted();

    /// <summary>Whether the Board window should stay always-on-top. The window itself applies
    /// this — this seam only carries the persisted preference.</summary>
    bool AlwaysOnTop { get; }

    /// <summary>Set the always-on-top preference and remember it in memory. Does not write to
    /// disk — call <see cref="SaveSettings"/> to persist.</summary>
    void SetAlwaysOnTop(bool value);

    /// <summary>If true (the default), a new phrase trigger replaces the one currently playing; if
    /// false, the new trigger is ignored while a phrase is already playing. Read once when the
    /// engine builds the phrase player, so a change here takes effect on the next restart.</summary>
    bool ReplaceOnRetrigger { get; }

    /// <summary>Set the retrigger preference and remember it in memory. Does not write to disk —
    /// call <see cref="SaveSettings"/> to persist.</summary>
    void SetReplaceOnRetrigger(bool value);

    /// <summary>The UI language code ("en", "uk", or "pl"). Applies on restart — choosing another
    /// language does not change any displayed text until the localization retrofit lands.</summary>
    string Language { get; }

    /// <summary>Set the language preference and remember it in memory. Does not write to disk —
    /// call <see cref="SaveSettings"/> to persist.</summary>
    void SetLanguage(string code);

    /// <summary>Theme preference: "system" (follow the OS), "light", or "dark".</summary>
    string Theme { get; }

    /// <summary>Set the theme preference and remember it in memory. Does not write to disk —
    /// call <see cref="SaveSettings"/> to persist.</summary>
    void SetTheme(string value);

    /// <summary>Export the library (metadata + active phrase WAVs) to a zip. Returns the number of
    /// version recordings that were not included (v1 limitation) — 0 if the export was complete.</summary>
    int Export(string destinationZipPath);

    /// <summary>Import a library archive (merge or replace). The in-session library refreshes on
    /// success — the Board's on-screen list does not (see the design spec's "Import refresh"
    /// note), so the caller should tell the operator a restart is needed to see the result.</summary>
    ImportResult Import(string sourceZipPath, ImportMode mode);

    /// <summary>True when the saved settings could not be read and were reset to defaults — false when
    /// they loaded cleanly. The board shows a localized notice at startup because the reset also drops
    /// the mic-calibration reference, which changes how loud phrases play (security scan 2026-07-12
    /// finding 3). Kept as a raw flag here since Host carries no display text — mirrors
    /// <see cref="ILibraryHost.LoadStatus"/>'s role for the library.</summary>
    bool SettingsWereReset { get; }

    /// <summary>The date of the newest daily backup, or null if none exist yet.</summary>
    DateOnly? LastBackupDate { get; }

    /// <summary>Open the backups folder in the OS file explorer.</summary>
    void OpenBackupFolder();
}
