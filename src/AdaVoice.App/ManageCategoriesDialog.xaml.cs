using System.Windows;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

/// <summary>Modal category manager. Its <c>DataContext</c> is a <c>CategoriesViewModel</c>; every change
/// (add / rename / delete) is persisted live by that view-model, so closing needs no save step.</summary>
public partial class ManageCategoriesDialog : Wpf.Ui.Controls.FluentWindow
{
    public ManageCategoriesDialog() => InitializeComponent();

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
}
