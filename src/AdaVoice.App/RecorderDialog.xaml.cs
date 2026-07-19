using System.ComponentModel;
using System.Windows.Threading;
using AdaVoice.App.Services;
using AdaVoice.App.ViewModels;
using Wpf.Ui;

namespace AdaVoice.App;

/// <summary>Modal recorder window. Its <c>DataContext</c> is the Board's own
/// <see cref="BoardViewModel"/> — recording state lives in one place whether or not this window
/// is open, so nothing is transferred on open/close.</summary>
public partial class RecorderDialog : Wpf.Ui.Controls.FluentWindow
{
    private readonly ContentDialogService _dialogService = new();

    // Purely cosmetic elapsed-time display while recording — lives here (not on BoardViewModel)
    // for the same reason EnvironmentChecksStepView's reveal timer does: nothing to unit-test,
    // and the board VM stays WPF-timer-free. Ticks at 100ms to match PendingTakeDurationLabel's
    // "0.0 s" precision.
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private DateTime _recordingStarted;

    public RecorderDialog()
    {
        InitializeComponent();
        _dialogService.SetDialogHost(RootDialogHost);
        _elapsedTimer.Tick += (_, _) => UpdateElapsed();
        // DataContext is set by the caller after the constructor runs (object-initializer syntax),
        // so subscribe once it's actually there — mirrors PhraseVersionsDialog/ManageCategoriesDialog.
        Loaded += (_, _) =>
        {
            if (DataContext is not BoardViewModel board)
                return;
            board.PropertyChanged += OnBoardPropertyChanged;
            // Sync to whatever the current state already is, not just future transitions — in
            // practice recording can only start after this dialog is already open (Start is the
            // dialog's own Idle-state button), so this never fires true in the real app, but it
            // keeps the dialog correct if that ever changes instead of silently showing no timer.
            SyncElapsedTimer(board.IsRecording);
        };
    }

    /// <summary>Confirm before discarding a pending take (CTRL-008 — Discard loses the recording,
    /// no undo). Wired into the long-lived <see cref="BoardViewModel"/> via
    /// <see cref="BoardViewModel.SetConfirmDiscard"/> every time a fresh <c>RecorderDialog</c>
    /// opens (<c>MainWindow.ShowRecorder</c>) — unlike a per-dialog VM, the board VM outlives any
    /// one dialog instance, so its confirm delegate must be re-pointed at this dialog's own host
    /// each time, not wired once.</summary>
    public Task<bool> ConfirmDiscardAsync() => DialogPrompts.ConfirmAsync(
        _dialogService, "Discard take", "Discard this take?\n\nThe recording will be lost.", "Discard");

    private void OnBoardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BoardViewModel.IsRecording) || DataContext is not BoardViewModel board)
            return;

        SyncElapsedTimer(board.IsRecording);
    }

    private void SyncElapsedTimer(bool isRecording)
    {
        if (isRecording)
        {
            _recordingStarted = DateTime.UtcNow;
            UpdateElapsed();
            _elapsedTimer.Start();
        }
        else
        {
            _elapsedTimer.Stop();
        }
    }

    private void UpdateElapsed() =>
        RecordingElapsedText.Text = $"{(DateTime.UtcNow - _recordingStarted).TotalSeconds:0.0} s";

    /// <summary>Closing mid-take stops the recorder instead of letting it run invisibly. The take
    /// becomes the pending one (kept in the view-model), so the operator's audio is never silently
    /// lost — it is waiting the next time the recorder opens. Also ends any "Add version" session
    /// (see <see cref="BoardViewModel.EndVersionRecordingSession"/>) — this window may have stayed
    /// open through several saved takes, so this is the one reliable point that session is over.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _elapsedTimer.Stop();
        if (DataContext is not BoardViewModel board)
            return;

        // BoardViewModel outlives this dialog — without this unsubscribe, every RecorderDialog
        // ever opened would leak forever via the board's long-lived PropertyChanged event.
        board.PropertyChanged -= OnBoardPropertyChanged;

        if (board.IsRecording)
            board.StopRecordingCommand.Execute(null);
        board.EndVersionRecordingSession();
    }
}
