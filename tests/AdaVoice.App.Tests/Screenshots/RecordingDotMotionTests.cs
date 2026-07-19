using System.Windows;
using System.Windows.Shapes;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;

namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// Phase D "motion": <c>RecordingDotStyle</c> (<c>Controls.xaml</c>) loops a breathe while
/// <c>BoardViewModel.IsRecording</c> is true (<c>RecorderDialog</c>'s recording row). Same
/// loop-shaped verification as <see cref="StateDotMotionTests"/> — a looping animation can't be
/// checked for its in-loop appearance (non-deterministic phase), so this proves the loop starts
/// (Opacity moves off its rest value) and, on <c>IsRecording</c> going false, correctly stops and
/// reverts (Opacity back at exactly 1, not stuck).
/// </summary>
[Collection(WpfAppCollection.Name)]
[Trait("Category", "Screenshot")]
public sealed class RecordingDotMotionTests(WpfAppFixture app)
{
    [ScreenshotFact]
    public void Recording_breathe_starts_and_reverts_cleanly()
    {
        RecorderDialog dialog = null!;
        BoardViewModel board = null!;

        app.Dispatcher.Invoke(() =>
        {
            App.ApplyTheme(app.Theme);
            var host = new FakePlaybackHost { State = EngineState.Live };
            var settingsHost = new FakeSettingsHost();
            board = new BoardViewModel(host, host, host, host, settingsHost,
                new StatusViewModel(host), new SettingsViewModel(settingsHost));
            dialog = new RecorderDialog { DataContext = board };
            dialog.Show();
        });

        app.Dispatcher.Invoke(() => board.IsRecording = true);

        // 300 ms into the 600 ms half-cycle (1 -> 0.5): comfortably mid-fade.
        Thread.Sleep(300);
        var midBreathe = app.Dispatcher.Invoke(() => ReadDotOpacity(dialog));

        app.Dispatcher.Invoke(() => board.IsRecording = false);
        Thread.Sleep(200);
        var afterExit = app.Dispatcher.Invoke(() => ReadDotOpacity(dialog));

        app.Dispatcher.Invoke(dialog.Close);

        Assert.True(midBreathe < 0.99, $"dot should be mid-breathe (<0.99), was {midBreathe}");
        Assert.True(afterExit > 0.99, $"dot should have reverted to 1 after recording stopped, was {afterExit}");
    }

    private static double ReadDotOpacity(Window window)
    {
        var style = (Style)Application.Current.Resources["RecordingDotStyle"]!;
        return VisualTreeSearch.FindDescendants<Ellipse>(window)
            .Single(e => ReferenceEquals(e.Style, style))
            .Opacity;
    }
}
