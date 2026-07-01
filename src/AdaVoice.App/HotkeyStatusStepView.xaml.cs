using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The hotkey-status step's view. Its <c>DataContext</c> is a
/// <c>HotkeyStatusStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class HotkeyStatusStepView : UserControl
{
    public HotkeyStatusStepView() => InitializeComponent();
}
