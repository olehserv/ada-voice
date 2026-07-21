using AdaVoice.Core.Storage;
using AdaVoice.Host;

namespace AdaVoice.App.Tests;

/// <summary>A test double for <see cref="ISettingsHost"/>: records duck changes and save calls.</summary>
internal class FakeSettingsHost : ISettingsHost
{
    public double MicDuckDb { get; set; } = -12;

    public List<double> SetCalls { get; } = [];
    public int SaveCount { get; private set; }

    public WindowPlacement? WindowPlacement { get; set; }
    public WindowPlacement? SavedPlacement { get; private set; }

    public void SetMicDuckDb(double db)
    {
        MicDuckDb = db;
        SetCalls.Add(db);
    }

    public void SaveSettings() => SaveCount++;

    public void SaveWindowPlacement(double width, double height, double left, double top) =>
        SavedPlacement = new WindowPlacement(width, height, left, top);

    public bool WizardCompleted { get; set; }
    public int MarkWizardCompletedCount { get; private set; }

    public void MarkWizardCompleted()
    {
        WizardCompleted = true;
        MarkWizardCompletedCount++;
    }

    public bool AlwaysOnTop { get; set; } = true;
    public int SetAlwaysOnTopCount { get; private set; }

    public void SetAlwaysOnTop(bool value)
    {
        AlwaysOnTop = value;
        SetAlwaysOnTopCount++;
    }

    public bool ReplaceOnRetrigger { get; set; } = true;
    public int SetReplaceOnRetriggerCount { get; private set; }

    public void SetReplaceOnRetrigger(bool value)
    {
        ReplaceOnRetrigger = value;
        SetReplaceOnRetriggerCount++;
    }

    public string Language { get; set; } = "en";

    public void SetLanguage(string code) => Language = code;

    public string Theme { get; set; } = "system";
    public int SetThemeCount { get; private set; }

    public void SetTheme(string value)
    {
        Theme = value;
        SetThemeCount++;
    }

    public string? ExportedPath { get; private set; }
    public int NextExportDroppedVersions { get; set; }
    public virtual int Export(string destinationZipPath)
    {
        ExportedPath = destinationZipPath;
        return NextExportDroppedVersions;
    }

    public (string Path, ImportMode Mode)? ImportedWith { get; private set; }
    public ImportResult NextImportResult { get; set; } = new(true, 1, 0);
    public virtual ImportResult Import(string sourceZipPath, ImportMode mode)
    {
        ImportedWith = (sourceZipPath, mode);
        return NextImportResult;
    }

    public bool SettingsWereReset { get; set; }

    public DateOnly? LastBackupDate { get; set; }
    public int OpenBackupFolderCount { get; private set; }
    public void OpenBackupFolder() => OpenBackupFolderCount++;
}
