using System.Windows;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

/// <summary>Modal conversation manager. Its <c>DataContext</c> is a <c>ConversationsViewModel</c>;
/// every change (add/rename/delete a conversation, add/remove/reorder a phrase) is persisted live by
/// that view-model, so closing needs no save step.</summary>
public partial class ManageConversationsDialog : Wpf.Ui.Controls.FluentWindow
{
    public ManageConversationsDialog() => InitializeComponent();

    // Auto-persist the conversation name on blur, mirroring SettingsWindow.xaml.cs's
    // DuckSlider_Committed pattern — no Save button to click.
    private void RowField_Committed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ConversationRowViewModel row } &&
            DataContext is ConversationsViewModel vm)
        {
            vm.RenameCommand.Execute(row);
        }
    }
}
