using System.ComponentModel;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

/// <summary>Modal recorder window. Its <c>DataContext</c> is the Board's own
/// <see cref="BoardViewModel"/> — recording state lives in one place whether or not this window
/// is open, so nothing is transferred on open/close.</summary>
public partial class RecorderDialog : Wpf.Ui.Controls.FluentWindow
{
    public RecorderDialog() => InitializeComponent();

    /// <summary>Closing mid-take stops the recorder instead of letting it run invisibly. The take
    /// becomes the pending one (kept in the view-model), so the operator's audio is never silently
    /// lost — it is waiting the next time the recorder opens.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is BoardViewModel board && board.IsRecording)
            board.StopRecordingCommand.Execute(null);
    }
}
