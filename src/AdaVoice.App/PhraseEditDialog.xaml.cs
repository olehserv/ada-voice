using System.Windows;

namespace AdaVoice.App;

/// <summary>Modal "Edit phrase" form. Its <c>DataContext</c> is a <c>PhraseEditViewModel</c>; the caller
/// reads the edited values back from that view-model when <see cref="Window.ShowDialog"/> returns true.</summary>
public partial class PhraseEditDialog : Window
{
    public PhraseEditDialog() => InitializeComponent();

    private void Save_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
