using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The first-call-confidence step's view. Its <c>DataContext</c> is a
/// <c>FirstCallStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class FirstCallStepView : UserControl
{
    public FirstCallStepView() => InitializeComponent();
}
