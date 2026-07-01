using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using AdaVoice.App.Services;
using AdaVoice.App.ViewModels;
using Serilog;
using Wpf.Ui.Controls;

namespace AdaVoice.App;

public partial class MainWindow : FluentWindow
{
    private HotkeyService? _hotkeys;

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

    // Persist the duck level only when the user finishes adjusting it (mouse drag end / focus loss),
    // so a drag does not write settings.json on every value change. Live apply happens via the binding.
    private void DuckSlider_DragCompleted(object sender, DragCompletedEventArgs e) => CommitSettings();

    private void DuckSlider_Committed(object sender, RoutedEventArgs e) => CommitSettings();

    private void CommitSettings() => (DataContext as BoardViewModel)?.Settings.Commit();
}
