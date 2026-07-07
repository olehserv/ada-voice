namespace AdaVoice.App;

/// <summary>Modal conversation manager. Its <c>DataContext</c> is a <c>ConversationsViewModel</c>;
/// every change (add/rename/delete a conversation, add/remove/reorder a phrase) is persisted live by
/// that view-model, so closing needs no save step.</summary>
public partial class ManageConversationsDialog : Wpf.Ui.Controls.FluentWindow
{
    public ManageConversationsDialog() => InitializeComponent();
}
