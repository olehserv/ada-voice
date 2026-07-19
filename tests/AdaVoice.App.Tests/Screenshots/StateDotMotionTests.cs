using System.Windows;
using System.Windows.Shapes;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;

namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// Phase D "motion": <c>StateDotStyle</c> (<c>Controls.xaml</c>) now loops a breathe on Live and a
/// hard blink on Degraded, and that one Style is applied to TWO simultaneously-live elements per
/// window — the Start/Stop toggle's inner dot and the status pill's own dot. A looping animation
/// can't be verified by sampling its in-loop appearance (it lands on an arbitrary phase every run,
/// by design), so this test verifies the one thing that actually matters: that each loop correctly
/// **starts** (Opacity moves away from its rest value of 1) while its trigger is active, and
/// correctly **stops and reverts** (Opacity is back at exactly 1, not stuck mid-loop) once the
/// trigger exits — for BOTH dots, proving the shared Style doesn't misbehave with two live
/// instances. This is the loop-shaped counterpart to <see cref="BackdropCrossfadeTests"/>'
/// one-shot "settles cleanly" check.
/// </summary>
[Collection(WpfAppCollection.Name)]
[Trait("Category", "Screenshot")]
public sealed class StateDotMotionTests(WpfAppFixture app)
{
    [ScreenshotFact]
    public void Live_breathe_starts_and_reverts_cleanly_on_both_dots()
    {
        MainWindow window = null!;
        FakePlaybackHost host = null!;

        app.Dispatcher.Invoke(() =>
        {
            App.ApplyTheme(app.Theme);
            host = new FakePlaybackHost { State = EngineState.Stopped };
            var settingsHost = new FakeSettingsHost();
            window = new MainWindow
            {
                DataContext = new BoardViewModel(host, host, host, host, settingsHost,
                    new StatusViewModel(host), new SettingsViewModel(settingsHost)),
            };
            window.Show();
        });

        app.Dispatcher.Invoke(() => host.RaiseStateChanged(EngineState.Live));

        // 300 ms into the 900 ms half-cycle (1 -> 0.5): comfortably mid-fade, nowhere near either
        // end, so this is robust to normal dispatcher/timing jitter.
        Thread.Sleep(300);
        var midBreathe = app.Dispatcher.Invoke(() => ReadDotOpacities(window));

        app.Dispatcher.Invoke(() => host.RaiseStateChanged(EngineState.Stopped));
        Thread.Sleep(200);
        var afterExit = app.Dispatcher.Invoke(() => ReadDotOpacities(window));

        app.Dispatcher.Invoke(window.Close);

        Assert.Equal(2, midBreathe.Count);
        foreach (var opacity in midBreathe)
            Assert.True(opacity < 0.99, $"dot should be mid-breathe (<0.99), was {opacity}");

        foreach (var opacity in afterExit)
            Assert.True(opacity > 0.99, $"dot should have reverted to 1 after exiting Live, was {opacity}");
    }

    [ScreenshotFact]
    public void Degraded_blink_starts_and_reverts_cleanly_on_both_dots()
    {
        MainWindow window = null!;
        FakePlaybackHost host = null!;

        app.Dispatcher.Invoke(() =>
        {
            App.ApplyTheme(app.Theme);
            host = new FakePlaybackHost { State = EngineState.Stopped };
            var settingsHost = new FakeSettingsHost();
            window = new MainWindow
            {
                DataContext = new BoardViewModel(host, host, host, host, settingsHost,
                    new StatusViewModel(host), new SettingsViewModel(settingsHost)),
            };
            window.Show();
        });

        app.Dispatcher.Invoke(() => host.RaiseStateChanged(EngineState.Degraded));

        // 500 ms: past the 400 ms on->off keyframe, comfortably before the 800 ms loop restart —
        // the dot should read the "off" value (0.2), not the rest value (1).
        Thread.Sleep(500);
        var midBlink = app.Dispatcher.Invoke(() => ReadDotOpacities(window));

        app.Dispatcher.Invoke(() => host.RaiseStateChanged(EngineState.OffAir));
        Thread.Sleep(200);
        var afterExit = app.Dispatcher.Invoke(() => ReadDotOpacities(window));

        app.Dispatcher.Invoke(window.Close);

        Assert.Equal(2, midBlink.Count);
        foreach (var opacity in midBlink)
            Assert.True(opacity < 0.5, $"dot should be in the blink's 'off' phase (<0.5), was {opacity}");

        foreach (var opacity in afterExit)
            Assert.True(opacity > 0.99, $"dot should have reverted to 1 after exiting Degraded, was {opacity}");
    }

    /// <summary>Both dots share StateDotStyle by StaticResource — reference-equality identifies
    /// them without needing x:Name on either Ellipse.</summary>
    private static List<double> ReadDotOpacities(MainWindow window)
    {
        var style = (Style)Application.Current.Resources["StateDotStyle"]!;
        return VisualTreeSearch.FindDescendants<Ellipse>(window)
            .Where(e => ReferenceEquals(e.Style, style))
            .Select(e => e.Opacity)
            .ToList();
    }
}
