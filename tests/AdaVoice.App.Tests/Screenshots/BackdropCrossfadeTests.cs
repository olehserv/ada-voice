using System.Windows;
using System.Windows.Controls;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;

namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// Phase D "motion": the state-lit backdrop (<c>Controls.xaml</c>'s <c>StateLayer*Style</c>/
/// <c>Bloom*Style</c>) now crossfades Opacity via <c>EnterActions</c>/<c>ExitActions</c> instead of a
/// plain Setter. A screenshot can only prove one settled rest state looks right — it can't prove a
/// layer never gets STUCK mid-fade when <c>Status.State</c> changes faster than the crossfade
/// (<c>Motion.State</c>, 500 ms) can complete, e.g. an interrupted <c>ExitActions</c> leaving a
/// previous state's tint bleeding through the current one. This test drives real, rapid state changes
/// (60 ms apart — well under 500 ms, deliberately interrupting each animation before it finishes) and
/// then reads each backdrop Border's live <c>Opacity</c> directly off the visual tree once things
/// settle: exactly the final state's two layers (a <c>StateLayer*Style</c> + its <c>Bloom*Style</c>,
/// except Stopped which has no bloom) should be ~1, every other layer ~0. Reading Opacity straight off
/// the DependencyProperty is more precise than pixel-sampling a screenshot for this specific check —
/// no image, no theme/DPI variables, just the value the animation actually left behind.
/// </summary>
[Collection(WpfAppCollection.Name)]
[Trait("Category", "Screenshot")]
public sealed class BackdropCrossfadeTests(WpfAppFixture app)
{
    [ScreenshotFact]
    public void Rapid_state_cycling_leaves_exactly_one_backdrop_layer_visible()
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

        // Faster than Motion.State (500 ms) on purpose — each change interrupts the previous
        // crossfade before it can finish, the exact condition that would expose a missing/incorrect
        // ExitActions leaving a layer's Opacity stuck above 0.
        EngineState[] cycle =
        [
            EngineState.Live, EngineState.OffAir, EngineState.Degraded,
            EngineState.Live, EngineState.Stopped, EngineState.Degraded, EngineState.Live,
        ];
        foreach (var state in cycle)
        {
            app.Dispatcher.Invoke(() => host.RaiseStateChanged(state));
            Thread.Sleep(60);
        }

        // Let the FINAL state's crossfade fully settle (matches ScreenshotHarness's own margin over
        // Motion.State).
        app.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        Thread.Sleep(700);

        var opacities = app.Dispatcher.Invoke(() => ReadBackdropOpacities(window));
        app.Dispatcher.Invoke(window.Close);

        // Final state is Live (last entry in `cycle`): its two layers should have faded fully in,
        // every other state's layer should have faded fully back out — none stuck mid-fade.
        Assert.True(opacities["StateLayerLiveStyle"] > 0.9,
            $"StateLayerLiveStyle should be visible, was {opacities["StateLayerLiveStyle"]}");
        Assert.True(opacities["BloomLiveStyle"] > 0.9,
            $"BloomLiveStyle should be visible, was {opacities["BloomLiveStyle"]}");

        foreach (var key in new[]
                 {
                     "StateLayerOffAirStyle", "StateLayerDegradedStyle", "StateLayerStoppedStyle",
                     "BloomOffAirStyle", "BloomDegradedStyle",
                 })
        {
            Assert.True(opacities[key] < 0.1, $"{key} should have faded out, was {opacities[key]}");
        }
    }

    /// <summary>
    /// Finds the backdrop Grid's Border children and keys each one's live Opacity by the Style
    /// resource it was declared with (StaticResource, so reference-equality is a reliable identity
    /// check without needing x:Name on every layer).
    /// </summary>
    private static Dictionary<string, double> ReadBackdropOpacities(MainWindow window)
    {
        string[] keys =
        [
            "StateLayerLiveStyle", "StateLayerOffAirStyle", "StateLayerDegradedStyle",
            "StateLayerStoppedStyle", "BloomLiveStyle", "BloomOffAirStyle", "BloomDegradedStyle",
        ];

        var borders = FindDescendants<Border>(window)
            .Where(b => b.Style is not null)
            .ToList();

        var result = new Dictionary<string, double>();
        foreach (var key in keys)
        {
            var style = (Style)Application.Current.Resources[key]!;
            var border = borders.Single(b => ReferenceEquals(b.Style, style));
            result[key] = border.Opacity;
        }

        return result;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindDescendants<T>(child))
                yield return descendant;
        }
    }
}
