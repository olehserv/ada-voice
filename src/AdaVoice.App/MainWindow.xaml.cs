using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AdaVoice.App.Resources;
using AdaVoice.App.Services;
using AdaVoice.App.ViewModels;
using AdaVoice.Host;
using Microsoft.Win32;
using Serilog;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace AdaVoice.App;

public partial class MainWindow : FluentWindow
{
    private HotkeyService? _hotkeys;

    // Backs the one in-flow Fluent dialog this window raises today (the board's delete confirm,
    // Pass 2b / audit E2). Hosted by RootDialogHost, wired in the constructor.
    private readonly ContentDialogService _dialogService = new();

    /// <summary>The stop hotkey label <see cref="HotkeyService"/> resolved on load ("Pause",
    /// "Ctrl+F12", or null if neither could be registered). Read by the setup wizard's hotkey step.</summary>
    public string? ActiveHotkey => _hotkeys?.ActiveHotkey;

    public MainWindow()
    {
        InitializeComponent();
        _dialogService.SetDialogHost(RootDialogHost);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BoardViewModel board)
        {
            board.Saved += OnPhraseSaved;
            board.Deleted += OnPhraseDeleted;
            board.Notified += OnNotified;

            // The library-load warning is set in the view-model's constructor, before this
            // window could subscribe — surface it now, with extra time (it explains why the
            // board may look empty).
            if (board.Notice is { } libraryWarning)
                ShowToast(libraryWarning, ControlAppearance.Caution, TimeSpan.FromSeconds(8));
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

    /// <summary>Ctrl+F focuses the search box (design 05 §3, keyboard-first). A window-level
    /// KeyBinding can't do this — focusing a control isn't a view-model command — so it lives here.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    /// <summary>Confirm a delete (the board calls this before orphaning the WAV) as a Fluent
    /// in-window dialog (Pass 2b / audit E2), following CTRL-008: the confirm is the whole method,
    /// Primary names the action, and the caller awaits before mutating anything.</summary>
    public async Task<bool> ConfirmDelete(PhraseItemViewModel item)
    {
        var result = await _dialogService.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = Strings.Main_DeletePhraseTitle,
            Content = string.Format(Strings.Main_DeletePhraseConfirmFormat, item.Title),
            PrimaryButtonText = Strings.Main_Delete,
            CloseButtonText = Strings.DialogPrompts_Cancel,
        });
        return result == ContentDialogResult.Primary;
    }

    /// <summary>Show the modal edit form; returns true if the user pressed Save.</summary>
    public bool ShowEditDialog(PhraseEditViewModel edit) =>
        new PhraseEditDialog { DataContext = edit, Owner = this }.ShowDialog() == true;

    /// <summary>Show the modal Versions window (every edit inside it persists immediately, so nothing
    /// is returned — the caller re-reads the phrase from the library after this returns). Wires the
    /// dialog's own <see cref="PhraseVersionsDialog.ConfirmDeleteAsync"/> into the already-built
    /// view-model once the window exists — it can't exist before (same reasoning as
    /// <see cref="ShowManageCategories"/>), but here as a post-construction setter rather than a
    /// constructor param, since <c>versions</c> arrives pre-built (it needs
    /// <c>BoardViewModel.RecordVersionForPhrase</c>, not available to this window).</summary>
    public void ShowVersionsDialog(PhraseVersionsViewModel versions)
    {
        var window = new PhraseVersionsDialog { DataContext = versions, Owner = this };
        versions.SetConfirmDelete(window.ConfirmDeleteAsync);
        window.ShowDialog();
    }

    /// <summary>Show the modal repair-phrase prompt; returns true if the operator chose an action
    /// (Re-record or Remove), false if they cancelled.</summary>
    public bool ShowRepairDialog(RepairPhraseViewModel repair) =>
        new RepairPhraseDialog { DataContext = repair, Owner = this }.ShowDialog() == true;

    /// <summary>Show the modal category manager (changes persist live, so nothing is returned). Builds
    /// the view-model here (not in <c>BoardViewModel.ManageCategories</c>) because its delete-confirm
    /// delegate is the new dialog's own <see cref="ManageCategoriesDialog.ConfirmDeleteAsync"/> — it
    /// can't exist before the window does (same reasoning as <see cref="ShowSettings"/>).</summary>
    public void ShowManageCategories(ILibraryHost library)
    {
        var window = new ManageCategoriesDialog { Owner = this };
        window.DataContext = new CategoriesViewModel(library, window.ConfirmDeleteAsync);
        window.ShowDialog();
    }

    /// <summary>Show the modal conversation manager (changes persist live, so nothing is returned).
    /// Builds the view-model here for the same reason as <see cref="ShowManageCategories"/> — its
    /// delete-confirm delegate is the new dialog's own async method.</summary>
    public void ShowManageConversations(ILibraryHost library)
    {
        var window = new ManageConversationsDialog { Owner = this };
        window.DataContext = new ConversationsViewModel(library, window.ConfirmDeleteAsync);
        window.ShowDialog();
    }

    /// <summary>Open the Categories filter menu: "Manage categories…", then one checkable row per
    /// category. Built fresh on every click (cheap for a handful of rows) so it never shows stale
    /// state. Native ContextMenu + checkable MenuItems, not data-bound — WPF has no clean way to mix
    /// a fixed action row with a dynamically-bound checkable list in one ItemsSource.</summary>
    private void ShowCategoryFilterMenu(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BoardViewModel board || sender is not FrameworkElement button)
            return;

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = button };
        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = Strings.Main_ManageCategoriesMenuItem,
            Command = board.ManageCategoriesCommand,
        });
        menu.Items.Add(new System.Windows.Controls.Separator());

        foreach (var item in board.CategoryFilterItems)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = CategoryDisplay.NameOf(item.Category),
                IsCheckable = true,
                IsChecked = item.IsChecked,
            };
            menuItem.Checked += (_, _) => item.IsChecked = true;
            menuItem.Unchecked += (_, _) => item.IsChecked = false;
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    /// <summary>Open the Conversations filter menu: "Manage conversations…", then one row per
    /// conversation (including the "None" sentinel, rendered like any other row). Clicking a row
    /// goes through BoardViewModel.ActivateConversation, not a direct property assignment — the
    /// same conversation clicked twice must still reset the step pointer, and a plain assignment
    /// would silently no-op on an equal (record) value. The menu does not track checked-state
    /// itself, it only reflects the current selection when built.</summary>
    private void ShowConversationFilterMenu(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BoardViewModel board || sender is not FrameworkElement button)
            return;

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = button };
        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = Strings.Main_ManageConversationsMenuItem,
            Command = board.ManageConversationsCommand,
        });
        menu.Items.Add(new System.Windows.Controls.Separator());

        foreach (var conversation in board.ConversationFilterOptions)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = conversation.Name,
                IsCheckable = true,
                IsChecked = board.SelectedConversationFilter.Id == conversation.Id,
            };
            menuItem.Click += (_, _) => board.ActivateConversation(conversation);
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    private RecorderDialog? _recorder;

    /// <summary>Show the modal recorder. It binds to the same BoardViewModel as the Board, so no
    /// state moves on open/close. No-ops while already open: the Record button inside the dialog
    /// re-enters StartRecording, which calls back here for a second window.</summary>
    public void ShowRecorder()
    {
        if (_recorder is not null)
            return;

        _recorder = new RecorderDialog { DataContext = DataContext, Owner = this };
        // The board VM outlives this dialog, so its discard-confirm must be re-pointed at this
        // fresh dialog's own host every time — the previous dialog's host (if any) is already gone.
        if (DataContext is BoardViewModel board)
            board.SetConfirmDiscard(_recorder.ConfirmDiscardAsync);
        try
        {
            _recorder.ShowDialog();
        }
        finally
        {
            _recorder = null;
        }
    }

    /// <summary>Show the modal setup wizard. If she reaches Finish (not just closes early), mark
    /// the wizard completed so it does not auto-show again on the next launch.</summary>
    public void ShowSetupWizard(SetupWizardViewModel wizard)
    {
        var window = new SetupWizardWindow { DataContext = wizard, Owner = this };
        if (window.ShowDialog() == true)
            (DataContext as BoardViewModel)?.Settings.MarkWizardCompleted();
    }

    /// <summary>Show the modal Settings window. Builds the <see cref="SettingsWindowViewModel"/>
    /// here (not in <c>BoardViewModel.RunSettings</c>) because its 4 dialog-prompt delegates
    /// (Pass 2b) are the new <see cref="SettingsWindow"/> instance's own async methods — they
    /// can't exist before the window does. Always-on-top changes apply live to this window as the
    /// operator toggles them — <c>Window.Topmost</c> is a WPF concept the view-model does not touch,
    /// so this window applies it on the view-model's behalf.</summary>
    public void ShowSettings(ISettingsHost settingsHost, ISetupHost setup, string? activeHotkey, Func<string?> pickExportPath)
    {
        var window = new SettingsWindow { Owner = this };
        var vm = new SettingsWindowViewModel(
            settingsHost, setup, activeHotkey, pickExportPath,
            window.PickImportFileAsync, window.ConfirmAndRestartAsync,
            window.ShowErrorAsync, window.ShowInfoAsync);

        vm.Behavior.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BehaviorSettingsViewModel.AlwaysOnTop))
                Topmost = vm.Behavior.AlwaysOnTop;
        };

        vm.Appearance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppearanceSettingsViewModel.Theme))
                App.ApplyThemePreference(vm.Appearance.Theme, this);
        };

        window.DataContext = vm;
        window.ShowDialog();
    }

    /// <summary>Ask where to save a library export. Returns null if the operator cancels.</summary>
    public string? PickExportPath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = Strings.Main_ExportFilter,
            FileName = $"adavoice-export-{DateTime.Now:yyyy-MM-dd}.zip",
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void SetUpStopHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hotkeys = new HotkeyService(new Win32HotkeyRegistrar(hwnd));
        _hotkeys.StopRequested += (_, _) => (DataContext as BoardViewModel)?.StopCommand.Execute(null);

        if (_hotkeys.Register())
        {
            Log.Information("Stop hotkey registered: {Key}", _hotkeys.ActiveHotkey);
            HotkeyHint.Text = string.Format(Strings.Main_HotkeyHintFormat, _hotkeys.ActiveHotkey);
            HotkeyHint.Visibility = Visibility.Visible;
        }
        else
        {
            Log.Warning("Stop hotkey unavailable: Pause and Ctrl+F12 are both taken");
            ShowToast(Strings.Main_UseOnScreenStop, ControlAppearance.Caution, TimeSpan.FromSeconds(5),
                toastTitle: Strings.Main_HotkeyUnavailableTitle);
        }
    }

    /// <summary>Board notifications ("Take discarded.", "No signal…", engine hints) as toasts,
    /// colored by severity. Errors linger longer — they carry a recovery instruction.</summary>
    private void OnNotified(object? sender, BoardNotification notification) =>
        ShowToast(
            notification.Message,
            notification.Severity switch
            {
                NoticeSeverity.Error => ControlAppearance.Danger,
                NoticeSeverity.Warning => ControlAppearance.Caution,
                _ => ControlAppearance.Secondary,
            },
            TimeSpan.FromSeconds(notification.Severity == NoticeSeverity.Error ? 6 : 4));

    private (string? Title, string Message, ControlAppearance Appearance)? _activeToast;
    private Snackbar? _activeSnackbar;

    /// <summary>Shows a toast, replacing whatever is currently showing — toasts never stack. A repeat
    /// of the exact same title/message/appearance while it is still on screen (e.g. mashing Record
    /// while the engine is stopped) is ignored instead of re-triggering it, so it stops spamming;
    /// a genuinely different toast still immediately takes over.</summary>
    private void ShowToast(string message, ControlAppearance appearance, TimeSpan timeout, string? toastTitle = null)
    {
        var key = (toastTitle, message, appearance);
        if (_activeSnackbar is { IsShown: true } && _activeToast == key)
            return;

        // Hide() is protected; IsShown has a public setter and drives the same close animation.
        if (_activeSnackbar is not null)
            _activeSnackbar.IsShown = false;
        _activeToast = key;
        _activeSnackbar = new Snackbar(RootSnackbar)
        {
            Title = toastTitle,
            Content = message,
            Appearance = appearance,
            Timeout = timeout,
        };
        _activeSnackbar.Show();
    }

    // Fires on the UI thread (SaveTake runs from a command), so showing the toast here is safe.
    private void OnPhraseSaved(object? sender, string title) =>
        ShowToast(title, ControlAppearance.Success, TimeSpan.FromSeconds(3), toastTitle: Strings.Main_SavedToastTitle);

    private void OnPhraseDeleted(object? sender, string title) =>
        ShowToast(title, ControlAppearance.Caution, TimeSpan.FromSeconds(3), toastTitle: Strings.Main_DeletedToastTitle);
}
