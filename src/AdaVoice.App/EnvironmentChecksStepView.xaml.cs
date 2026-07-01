using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The environment-checks step's view. Its <c>DataContext</c> is an
/// <c>EnvironmentChecksStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class EnvironmentChecksStepView : UserControl
{
    public EnvironmentChecksStepView() => InitializeComponent();
}
