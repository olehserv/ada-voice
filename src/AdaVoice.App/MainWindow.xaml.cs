using System.Windows;
using System.Windows.Controls.Primitives;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // Persist the duck level only when the user finishes adjusting it (mouse drag end / focus loss),
    // so a drag does not write settings.json on every value change. Live apply happens via the binding.
    private void DuckSlider_DragCompleted(object sender, DragCompletedEventArgs e) => CommitSettings();

    private void DuckSlider_Committed(object sender, RoutedEventArgs e) => CommitSettings();

    private void CommitSettings() => (DataContext as BoardViewModel)?.Settings.Commit();
}
