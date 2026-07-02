using System;
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
    private readonly Func<(string Path, ImportMode Mode)?> _pickImportFile;
    private readonly Action _confirmAndRestart;
    private readonly Action<string> _showError;
    private readonly Action<string> _showInfo;

    [ObservableProperty]
    private string _language;

    public BackupSettingsViewModel(
        ISettingsHost settings,
        Func<string?> pickExportPath,
        Func<(string Path, ImportMode Mode)?> pickImportFile,
        Action confirmAndRestart,
        Action<string> showError,
        Action<string> showInfo)
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
    private void Export()
    {
        var path = _pickExportPath();
        if (path is null)
            return; // cancelled

        try
        {
            _settings.Export(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _showError($"Could not export: {ex.Message}");
        }
    }

    [RelayCommand]
    private void Import()
    {
        var picked = _pickImportFile();
        if (picked is not { } choice)
            return; // cancelled

        ImportResult result;
        try
        {
            result = _settings.Import(choice.Path, choice.Mode);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _showError($"Could not import: {ex.Message}");
            return;
        }

        if (!result.Success)
            _showError($"Could not import: {result.Error}");
        else
            _showInfo($"Imported {result.Added} phrase(s) ({result.Skipped} skipped). " +
                "Restart AdaVoice to see them on your board.");
    }

    [RelayCommand]
    private void OpenBackupFolder() => _settings.OpenBackupFolder();

    partial void OnLanguageChanged(string value)
    {
        _settings.SetLanguage(value);
        _settings.SaveSettings();
        _confirmAndRestart();
    }
}
