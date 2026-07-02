using AdaVoice.Core.Storage;
using AdaVoice.Host;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Composition root for the Settings window: builds the three group view-models the window's
/// three sections bind to, all sharing the same <see cref="ISettingsHost"/>. Owns nothing itself.
/// </summary>
public sealed class SettingsWindowViewModel
{
    public SettingsWindowViewModel(
        ISettingsHost settings,
        ISetupHost setup,
        string? activeHotkey,
        Func<string?> pickExportPath,
        Func<(string Path, ImportMode Mode)?> pickImportFile,
        Action confirmAndRestart,
        Action<string> showError,
        Action<string> showInfo)
    {
        Levels = new LevelsSettingsViewModel(settings, setup);
        Behavior = new BehaviorSettingsViewModel(settings, activeHotkey);
        Backup = new BackupSettingsViewModel(settings, pickExportPath, pickImportFile, confirmAndRestart, showError, showInfo);
    }

    public LevelsSettingsViewModel Levels { get; }
    public BehaviorSettingsViewModel Behavior { get; }
    public BackupSettingsViewModel Backup { get; }
}
