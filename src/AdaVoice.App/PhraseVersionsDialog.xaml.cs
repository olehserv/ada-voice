using System.Windows;
using AdaVoice.App.ViewModels;
using Wpf.Ui.Controls;

namespace AdaVoice.App;

/// <summary>The "Versions" window: a board-like tile grid of one phrase's primary recording and its
/// alternate takes. Its <c>DataContext</c> is a <c>PhraseVersionsViewModel</c>. Every edit inside it
/// (rename/delete) persists immediately, so unlike <see cref="PhraseEditDialog"/> there is no
/// Save/Cancel distinction — closing the window (by any means) is always "done". "Add version" opens
/// the recorder on top of this window (see <c>RecorderDialog</c>) without closing it — recording
/// happens entirely inside <see cref="PhraseVersionsViewModel.RecordVersionCommand"/>.</summary>
public partial class PhraseVersionsDialog : Wpf.Ui.Controls.FluentWindow
{
    /// <summary>Stop any tile still previewing when the window closes, whatever triggered the close
    /// (Close button, title-bar X, Escape/IsCancel) — otherwise headphone audio outlives the window.</summary>
    public PhraseVersionsDialog()
    {
        InitializeComponent();
        Closed += (_, _) => ((PhraseVersionsViewModel)DataContext).StopPreview();
        // DataContext is set by the caller after the constructor runs (object-initializer syntax),
        // so subscribe once it's actually there — mirrors MainWindow.OnLoaded.
        Loaded += (_, _) =>
        {
            if (DataContext is PhraseVersionsViewModel versions)
                versions.Notified += OnNotified;
        };
    }

    /// <summary>A preview failure (missing file, bad device) as a toast — this window previously had
    /// no toast channel and swallowed the failure entirely (review finding 4).</summary>
    private void OnNotified(object? sender, BoardNotification notification) =>
        new Snackbar(RootSnackbar)
        {
            Content = notification.Message,
            Appearance = notification.Severity == NoticeSeverity.Error ? ControlAppearance.Danger : ControlAppearance.Caution,
            Timeout = TimeSpan.FromSeconds(notification.Severity == NoticeSeverity.Error ? 6 : 4),
        }.Show();
}
