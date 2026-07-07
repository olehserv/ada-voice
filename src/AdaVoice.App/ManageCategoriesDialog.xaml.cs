using System.Windows;

namespace AdaVoice.App;

/// <summary>Modal category manager. Its <c>DataContext</c> is a <c>CategoriesViewModel</c>; every change
/// (add / rename / delete) is persisted live by that view-model, so closing needs no save step.</summary>
public partial class ManageCategoriesDialog : Wpf.Ui.Controls.FluentWindow
{
    public ManageCategoriesDialog() => InitializeComponent();
}
