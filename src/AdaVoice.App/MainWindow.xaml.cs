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
