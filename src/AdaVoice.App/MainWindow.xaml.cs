using System.Windows;
using System.Windows.Controls.Primitives;
using AdaVoice.App.ViewModels;
using Wpf.Ui.Controls;

namespace AdaVoice.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is BoardViewModel board)
            board.Saved += OnPhraseSaved;
    }

    // Fires on the UI thread (SaveTake runs from a command), so showing the toast here is safe.
    private void OnPhraseSaved(object? sender, string title) =>
        new Snackbar(RootSnackbar)
        {
            Title = "Saved",
            Content = title,
            Appearance = ControlAppearance.Success,
            Timeout = TimeSpan.FromSeconds(3),
        }.Show();

    // Persist the duck level only when the user finishes adjusting it (mouse drag end / focus loss),
    // so a drag does not write settings.json on every value change. Live apply happens via the binding.
    private void DuckSlider_DragCompleted(object sender, DragCompletedEventArgs e) => CommitSettings();

    private void DuckSlider_Committed(object sender, RoutedEventArgs e) => CommitSettings();

    private void CommitSettings() => (DataContext as BoardViewModel)?.Settings.Commit();
}
