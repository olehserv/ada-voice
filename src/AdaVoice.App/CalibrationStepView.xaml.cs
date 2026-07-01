using System.Windows.Controls;

namespace AdaVoice.App;

/// <summary>The voice-calibration step's view. Its <c>DataContext</c> is a
/// <c>CalibrationStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class CalibrationStepView : UserControl
{
    public CalibrationStepView() => InitializeComponent();
}
