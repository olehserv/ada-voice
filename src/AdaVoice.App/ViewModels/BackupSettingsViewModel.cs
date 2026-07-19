using System.IO;
using AdaVoice.Core.Storage;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>Settings window: the Language &amp; Backup group. Every WPF-specific action (file
/// dialogs, the restart confirmation, error display) is owned by the window via injected
/// delegates, so this view-model stays unit-testable with fakes.</summary>
public partial class BackupSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _settings;
    private readonly Func<string?> _pickExportPath;
    private readonly Func<Task<(string Path, ImportMode Mode)?>> _pickImportFile;
    private readonly Func<Task> _confirmAndRestart;
    private readonly Func<string, Task> _showError;
    private readonly Func<string, Task> _showInfo;

    [ObservableProperty]
    private string _language;

    public BackupSettingsViewModel(
        ISettingsHost settings,
        Func<string?> pickExportPath,
        Func<Task<(string Path, ImportMode Mode)?>> pickImportFile,
        Func<Task> confirmAndRestart,
        Func<string, Task> showError,
        Func<string, Task> showInfo)
    {
        _settings = settings;
        _pickExportPath = pickExportPath;
        _pickImportFile = pickImportFile;
        _confirmAndRestart = confirmAndRestart;
        _showError = showError;
        _showInfo = showInfo;
        _language = settings.Language;
        LastBackupDate = settings.LastBackupDate;
    }

    /// <summary>The date of the newest daily backup, or null if none exist yet — read once when
    /// the window opens.</summary>
    public DateOnly? LastBackupDate { get; }

    [RelayCommand]
    private async Task Export()
    {
        var path = _pickExportPath();
        if (path is null)
            return; // cancelled

        try
        {
            var droppedVersions = _settings.Export(path);
            // Export never includes version recordings (v1 limitation) — say so when it happened, so
            // the operator doesn't assume the export was complete (review finding 2).
            if (droppedVersions > 0)
                await _showInfo($"Exported. {droppedVersions} alternate take(s) were not included — " +
                    "phrase versions aren't included in exports yet.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _showError($"Could not export: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task Import()
    {
        var picked = await _pickImportFile();
        if (picked is not { } choice)
            return; // cancelled

        ImportResult result;
        try
        {
            result = _settings.Import(choice.Path, choice.Mode);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await _showError($"Could not import: {ex.Message}");
            return;
        }

        if (!result.Success)
            await _showError($"Could not import: {result.Error}");
        else
            await _showInfo($"Imported {result.Added} phrase(s) ({result.Skipped} skipped). " +
                "Restart AdaVoice to see them on your board.");
    }

    [RelayCommand]
    private void OpenBackupFolder() => _settings.OpenBackupFolder();

    partial void OnLanguageChanged(string value)
    {
        _settings.SetLanguage(value);
        _settings.SaveSettings();
        _ = _confirmAndRestart(); // OnLanguageChanged is a generated partial void — cannot be async
    }
}
