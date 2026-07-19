using System.Windows;
using AdaVoice.App.Services;
using AdaVoice.App.ViewModels;
using Wpf.Ui;

namespace AdaVoice.App;

/// <summary>Modal conversation manager. Its <c>DataContext</c> is a <c>ConversationsViewModel</c>;
/// every change (add/rename/delete a conversation, add/remove/reorder a phrase) is persisted live by
/// that view-model, so closing needs no save step.</summary>
public partial class ManageConversationsDialog : Wpf.Ui.Controls.FluentWindow
{
    // Backs this window's own in-flow Fluent delete-confirm. Hosted by RootDialogHost, wired in
    // the constructor — mirrors MainWindow's ConfirmDelete pattern; this window needs its own
    // host because it is itself a modal child of MainWindow.
    private readonly ContentDialogService _dialogService = new();

    public ManageConversationsDialog()
    {
        InitializeComponent();
        _dialogService.SetDialogHost(RootDialogHost);
    }

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

    /// <summary>Confirm before deleting a conversation.</summary>
    public Task<bool> ConfirmDeleteAsync(ConversationRowViewModel row) => DialogPrompts.ConfirmAsync(
        _dialogService, "Delete conversation", $"Delete \"{row.Name}\"?", "Delete");
}
