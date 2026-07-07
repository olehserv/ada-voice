using System.Windows;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

/// <summary>Modal setup wizard. Its <c>DataContext</c> is a <see cref="SetupWizardViewModel"/>; the
/// caller reads <see cref="Window.ShowDialog"/>'s result to know whether it was actually finished
/// (true) versus closed early (false/null) — driven by <see cref="SetupWizardViewModel.Finished"/>,
/// never a plain "window closed" signal.</summary>
public partial class SetupWizardWindow : Wpf.Ui.Controls.FluentWindow
{
    public SetupWizardWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is SetupWizardViewModel vm)
                vm.Finished += (_, _) => DialogResult = true;
        };
    }
}
