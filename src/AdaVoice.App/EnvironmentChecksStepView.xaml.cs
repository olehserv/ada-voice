using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

/// <summary>The environment-checks step's view. Its <c>DataContext</c> is an
/// <c>EnvironmentChecksStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class EnvironmentChecksStepView : UserControl
{
    // Purely cosmetic — the checks themselves run instantly, so this just gives the operator a
    // brief "it's working" moment before the results appear (design 05 §2).
    private readonly DispatcherTimer _revealTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public EnvironmentChecksStepView()
    {
        InitializeComponent();
        _revealTimer.Tick += (_, _) => Reveal();
        Loaded += (_, _) => RestartReveal();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is EnvironmentChecksStepViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is EnvironmentChecksStepViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-check pressed (Checks was reassigned) — show the spinner again before the new results.
        if (e.PropertyName == nameof(EnvironmentChecksStepViewModel.Checks))
            RestartReveal();
    }

    private void RestartReveal()
    {
        _revealTimer.Stop();
        SpinnerPanel.Visibility = Visibility.Visible;
        ChecksList.Visibility = Visibility.Collapsed;
        _revealTimer.Start();
    }

    private void Reveal()
    {
        _revealTimer.Stop();
        SpinnerPanel.Visibility = Visibility.Collapsed;
        ChecksList.Visibility = Visibility.Visible;
    }

    /// <summary>Opens the VB-CABLE download link in the operator's default browser. A pure OS
    /// action with nothing to unit-test, so it lives here rather than in the ViewModel or a new
    /// host seam — there is no business logic to isolate, just a single link click.</summary>
    private void OnVbCableLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
