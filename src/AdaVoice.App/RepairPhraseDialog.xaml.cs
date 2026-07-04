using System.Windows;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

/// <summary>Modal "repair phrase" prompt for a broken (audio-missing) phrase. Its
/// <c>DataContext</c> is a <c>RepairPhraseViewModel</c>; the caller reads
/// <see cref="RepairPhraseViewModel.Choice"/> after <see cref="Window.ShowDialog"/> returns true.</summary>
public partial class RepairPhraseDialog : Window
{
    public RepairPhraseDialog() => InitializeComponent();

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        ((RepairPhraseViewModel)DataContext).ChooseRemove();
        DialogResult = true;
    }

    private void ReRecord_Click(object sender, RoutedEventArgs e)
    {
        ((RepairPhraseViewModel)DataContext).ChooseReRecord();
        DialogResult = true;
    }
}
