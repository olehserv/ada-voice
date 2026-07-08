using AdaVoice.App.ViewModels;

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
    }
}
