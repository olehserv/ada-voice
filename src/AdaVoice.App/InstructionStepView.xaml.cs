using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The Chrome/Zoho instruction step's view. Its <c>DataContext</c> is an
/// <c>InstructionStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class InstructionStepView : UserControl
{
    public InstructionStepView() => InitializeComponent();
}
