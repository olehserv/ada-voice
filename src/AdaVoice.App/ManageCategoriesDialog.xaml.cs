using System.Windows;
using AdaVoice.App.Services;
using AdaVoice.App.ViewModels;
using Wpf.Ui;

namespace AdaVoice.App;

/// <summary>Modal category manager. Its <c>DataContext</c> is a <c>CategoriesViewModel</c>; every change
/// (add / rename / delete) is persisted live by that view-model, so closing needs no save step.</summary>
public partial class ManageCategoriesDialog : Wpf.Ui.Controls.FluentWindow
{
    // Backs this window's own in-flow Fluent delete-confirm. Hosted by RootDialogHost, wired in
    // the constructor — mirrors MainWindow's ConfirmDelete pattern; this window needs its own
    // host because it is itself a modal child of MainWindow.
    private readonly ContentDialogService _dialogService = new();

    public ManageCategoriesDialog()
    {
        InitializeComponent();
        _dialogService.SetDialogHost(RootDialogHost);
    }

    // Auto-persist a row on blur (name TextBox) or selection change (colour ComboBox), mirroring
    // SettingsWindow.xaml.cs's DuckSlider_Committed pattern — no per-row Save button to click.
    private void RowField_Committed(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CategoryRowViewModel row } &&
            DataContext is CategoriesViewModel vm)
        {
            vm.SaveCommand.Execute(row);
        }
    }

    /// <summary>Confirm before deleting a category — its phrases fall back to Uncategorized.</summary>
    public Task<bool> ConfirmDeleteAsync(CategoryRowViewModel row) => DialogPrompts.ConfirmAsync(
        _dialogService, "Delete category",
        $"Delete \"{row.Name}\"?\n\nIts phrases will move to Uncategorized.", "Delete");
}
