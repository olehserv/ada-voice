using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using AdaVoice.App.Services;
using AdaVoice.App.ViewModels;
using AdaVoice.Core.Storage;
using Microsoft.Win32;
using Serilog;
using Wpf.Ui.Controls;

namespace AdaVoice.App;

public partial class MainWindow : FluentWindow
{
    private HotkeyService? _hotkeys;

    /// <summary>The stop hotkey label <see cref="HotkeyService"/> resolved on load ("Pause",
    /// "Ctrl+F12", or null if neither could be registered). Read by the setup wizard's hotkey step.</summary>
    public string? ActiveHotkey => _hotkeys?.ActiveHotkey;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BoardViewModel board)
        {
            board.Saved += OnPhraseSaved;
            board.Deleted += OnPhraseDeleted;
        }

        SetUpStopHotkey();
        Closed += (_, _) => _hotkeys?.Dispose();
    }

    /// <summary>Restore the saved window size/position before the first render (so there is no flash),
    /// clamped to the current screens in case it was last closed on a monitor that is now unplugged.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if ((DataContext as BoardViewModel)?.Settings.WindowPlacement is { } saved)
        {
            // The virtual screen is the union of all monitors (WPF exposes it as four values, not a Rect).
            var p = saved.ClampTo(
                SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = p.Left;
            Top = p.Top;
            Width = p.Width;
            Height = p.Height;
        }
    }

    /// <summary>Remember where the operator left the window. Uses <see cref="Window.RestoreBounds"/> when
    /// minimized/maximized, so we never persist the off-screen (~ −32000) coordinates a minimized window
    /// reports.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        if (DataContext is BoardViewModel board)
        {
            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            board.Settings.SaveWindowPlacement(bounds.Width, bounds.Height, bounds.Left, bounds.Top);
        }
    }

    /// <summary>Confirm a delete (the board calls this before orphaning the WAV). Synchronous so it fits
    /// the view-model's <c>Func&lt;_, bool&gt;</c> callback.</summary>
    public bool ConfirmDelete(PhraseItemViewModel item) =>
        System.Windows.MessageBox.Show(
            this,
            $"Delete “{item.Title}”?\n\nThe recording is kept as a backup and can be recovered.",
            "Delete phrase",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;

    /// <summary>Show the modal edit form; returns true if the user pressed Save.</summary>
    public bool ShowEditDialog(PhraseEditViewModel edit) =>
        new PhraseEditDialog { DataContext = edit, Owner = this }.ShowDialog() == true;

    /// <summary>Show the modal category manager (changes persist live, so nothing is returned).</summary>
    public void ShowManageCategories(CategoriesViewModel categories) =>
        new ManageCategoriesDialog { DataContext = categories, Owner = this }.ShowDialog();

    /// <summary>Show the modal setup wizard. If she reaches Finish (not just closes early), mark
    /// the wizard completed so it does not auto-show again on the next launch.</summary>
    public void ShowSetupWizard(SetupWizardViewModel wizard)
    {
        var window = new SetupWizardWindow { DataContext = wizard, Owner = this };
        if (window.ShowDialog() == true)
            (DataContext as BoardViewModel)?.Settings.MarkWizardCompleted();
    }

    /// <summary>Show the modal Settings window. Always-on-top changes apply live to this window
    /// as the operator toggles them — <c>Window.Topmost</c> is a WPF concept the view-model does
    /// not touch, so this window applies it on the view-model's behalf.</summary>
    public void ShowSettings(SettingsWindowViewModel vm)
    {
        vm.Behavior.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BehaviorSettingsViewModel.AlwaysOnTop))
                Topmost = vm.Behavior.AlwaysOnTop;
        };

        new SettingsWindow { DataContext = vm, Owner = this }.ShowDialog();
    }

    /// <summary>Ask where to save a library export. Returns null if the operator cancels.</summary>
    public string? PickExportPath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "AdaVoice export (*.zip)|*.zip",
            FileName = $"adavoice-export-{DateTime.Now:yyyy-MM-dd}.zip",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    /// <summary>Ask which archive to import and whether to merge or replace. Returns null if the
    /// operator cancels at either step.</summary>
    public (string Path, ImportMode Mode)? PickImportFile()
    {
        var openDialog = new OpenFileDialog { Filter = "AdaVoice export (*.zip)|*.zip" };
        if (openDialog.ShowDialog(this) != true)
            return null;

        var choice = System.Windows.MessageBox.Show(
            this,
            "Merge with your current library, or replace it entirely?\n\n" +
            "Yes = Merge (keeps your current phrases)\nNo = Replace (your current library is overwritten)",
            "Import library",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);

        return choice switch
        {
            System.Windows.MessageBoxResult.Yes => (openDialog.FileName, ImportMode.Merge),
            System.Windows.MessageBoxResult.No => (openDialog.FileName, ImportMode.Replace),
            _ => null,
        };
    }

    /// <summary>Offer to restart now so a language change takes effect. Fails silently if the
    /// relaunch itself cannot start — the setting is already saved either way, so a failed restart
    /// must never block closing Settings.</summary>
    public void ConfirmAndRestart()
    {
        var restart = System.Windows.MessageBox.Show(
            this,
            "The language change takes effect after a restart. Restart AdaVoice now?",
            "Restart required",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;

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
    public void ShowError(string message) =>
        System.Windows.MessageBox.Show(this, message, "AdaVoice",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);

    /// <summary>Show an informational dialog (the Import-succeeded notice).</summary>
    public void ShowInfo(string message) =>
        System.Windows.MessageBox.Show(this, message, "AdaVoice",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);

    private void SetUpStopHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hotkeys = new HotkeyService(new Win32HotkeyRegistrar(hwnd));
        _hotkeys.StopRequested += (_, _) => (DataContext as BoardViewModel)?.StopCommand.Execute(null);

        if (_hotkeys.Register())
        {
            Log.Information("Stop hotkey registered: {Key}", _hotkeys.ActiveHotkey);
            HotkeyHint.Text = $"Or press {_hotkeys.ActiveHotkey} to stop from any window";
            HotkeyHint.Visibility = Visibility.Visible;
        }
        else
        {
            Log.Warning("Stop hotkey unavailable: Pause and Ctrl+F12 are both taken");
            new Snackbar(RootSnackbar)
            {
                Title = "Stop hotkey unavailable",
                Content = "Use the on-screen STOP button.",
                Appearance = ControlAppearance.Caution,
                Timeout = TimeSpan.FromSeconds(5),
            }.Show();
        }
    }

    // Fires on the UI thread (SaveTake runs from a command), so showing the toast here is safe.
    private void OnPhraseSaved(object? sender, string title) =>
        new Snackbar(RootSnackbar)
        {
            Title = "Saved",
            Content = title,
            Appearance = ControlAppearance.Success,
            Timeout = TimeSpan.FromSeconds(3),
        }.Show();

    private void OnPhraseDeleted(object? sender, string title) =>
        new Snackbar(RootSnackbar)
        {
            Title = "Deleted",
            Content = title,
            Appearance = ControlAppearance.Caution,
            Timeout = TimeSpan.FromSeconds(3),
        }.Show();
}
