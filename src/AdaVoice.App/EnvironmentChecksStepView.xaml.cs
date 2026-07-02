using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace AdaVoice.App;

/// <summary>The environment-checks step's view. Its <c>DataContext</c> is an
/// <c>EnvironmentChecksStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class EnvironmentChecksStepView : UserControl
{
    public EnvironmentChecksStepView() => InitializeComponent();

    /// <summary>Opens the VB-CABLE download link in the operator's default browser. A pure OS
    /// action with nothing to unit-test, so it lives here rather than in the ViewModel or a new
    /// host seam — there is no business logic to isolate, just a single link click.</summary>
    private void OnVbCableLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
