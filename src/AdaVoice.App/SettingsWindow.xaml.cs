using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using AdaVoice.App.Services;
using AdaVoice.App.ViewModels;
using AdaVoice.Core.Storage;
using Microsoft.Win32;
using Serilog;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace AdaVoice.App;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    // Backs this window's own in-flow Fluent dialogs (Pass 2b's 4 deferred prompts). Hosted by
    // RootDialogHost, wired in the constructor — mirrors MainWindow's ConfirmDelete pattern; this
    // window needs its own host because it is itself a modal child of MainWindow.
    private readonly ContentDialogService _dialogService = new();

    public SettingsWindow()
    {
        InitializeComponent();
        _dialogService.SetDialogHost(RootDialogHost);
    }

    // Persist the duck level only when the user finishes adjusting it (mouse drag end / focus
    // loss), so a drag does not write settings.json on every value change. Live apply happens via
    // the binding (same pattern the Board's status bar used before the slider moved here).
    private void DuckSlider_DragCompleted(object sender, DragCompletedEventArgs e) => CommitLevels();

    private void DuckSlider_Committed(object sender, RoutedEventArgs e) => CommitLevels();

    private void CommitLevels() => (DataContext as SettingsWindowViewModel)?.Levels.Commit();

    /// <summary>Ask which archive to import and whether to merge or replace. Returns null if the
    /// operator cancels at either step.</summary>
    public async Task<(string Path, ImportMode Mode)?> PickImportFileAsync()
    {
        var openDialog = new OpenFileDialog { Filter = "AdaVoice export (*.zip)|*.zip" };
        if (openDialog.ShowDialog(this) != true)
            return null;

        var result = await _dialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Import library",
            Content = "Merge with your current library, or replace it entirely?\n\n" +
                "Merge keeps your current phrases. Replace overwrites your current library.",
            PrimaryButtonText = "Merge",
            SecondaryButtonText = "Replace",
            CloseButtonText = "Cancel",
        });

        return result switch
        {
            ContentDialogResult.Primary => (openDialog.FileName, ImportMode.Merge),
            ContentDialogResult.Secondary => (openDialog.FileName, ImportMode.Replace),
            _ => null,
        };
    }

    /// <summary>Offer to restart now so a language change takes effect. Fails silently if the
    /// relaunch itself cannot start — the setting is already saved either way, so a failed restart
    /// must never block closing Settings.</summary>
    public async Task ConfirmAndRestartAsync()
    {
        var restart = await DialogPrompts.ConfirmAsync(_dialogService, "Restart required",
            "The language change takes effect after a restart. Restart AdaVoice now?", "Restart now");

        if (!restart)
            return;

        try
        {
            Process.Start(Environment.ProcessPath!);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Log.Warning(ex, "Could not restart automatically — the language change applies on the next manual launch");
        }
    }

    /// <summary>Show an error dialog (Export/Import failures).</summary>
    public Task ShowErrorAsync(string message) => DialogPrompts.ShowErrorAsync(_dialogService, message);

    /// <summary>Show an informational dialog (the Import-succeeded notice).</summary>
    public Task ShowInfoAsync(string message) => DialogPrompts.ShowInfoAsync(_dialogService, message);
}
