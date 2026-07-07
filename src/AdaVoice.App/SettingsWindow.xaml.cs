using System.Windows;
using System.Windows.Controls.Primitives;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

public partial class SettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    public SettingsWindow() => InitializeComponent();

    // Persist the duck level only when the user finishes adjusting it (mouse drag end / focus
    // loss), so a drag does not write settings.json on every value change. Live apply happens via
    // the binding (same pattern the Board's status bar used before the slider moved here).
    private void DuckSlider_DragCompleted(object sender, DragCompletedEventArgs e) => CommitLevels();

    private void DuckSlider_Committed(object sender, RoutedEventArgs e) => CommitLevels();

    private void CommitLevels() => (DataContext as SettingsWindowViewModel)?.Levels.Commit();
}
