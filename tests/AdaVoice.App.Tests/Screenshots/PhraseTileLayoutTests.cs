using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// Regression coverage for the Pass 6 fixed-tile-size bug found while re-verifying that pass: the
/// phrase tile's ui:Button has a fixed Width/Height (Theme/Controls.xaml's PhraseButtonStyle), but its
/// content used to size itself to fit rather than stretching to fill that box — WPF-UI's Button
/// template centers its ContentPresenter instead of honoring VerticalContentAlignment="Stretch" — so
/// the visible tile still grew/shrank with tag count and title length, silently reintroducing the
/// exact defect (audit F1) Pass 6 was meant to fix. Fixed by giving the tile's content-root Grid an
/// explicit Width/Height matching PhraseButtonStyle (MainWindow.xaml's tile DataTemplate).
///
/// This is a plain layout assertion, not a screenshot, but still needs a real shown Window for
/// Measure/Arrange to run — reuses the same "needs an interactive desktop" gate as the screenshot
/// tests (see ScreenshotFactAttribute) rather than inventing a separate one.
/// </summary>
[Collection(WpfAppCollection.Name)]
[Trait("Category", "Screenshot")]
public sealed class PhraseTileLayoutTests(WpfAppFixture app)
{
    [ScreenshotFact]
    public void Phrase_tiles_render_at_a_uniform_height_regardless_of_tags_or_title()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" }],
            Tags =
            [
                new TagInfo { Name = "vip", Color = "#E8B04B" },
                new TagInfo { Name = "opening", Color = "#4CC2FF" },
                new TagInfo { Name = "friendly", Color = "#57C7A6" },
            ],
            Phrases =
            [
                new PhraseEntry { Id = "no-tags", Title = "Short", CategoryId = Category.DefaultId, DurationMs = 1000 },
                new PhraseEntry
                {
                    Id = "has-tags", Title = "A genuinely long phrase title that wraps across two full lines",
                    CategoryId = Category.DefaultId, Tags = ["vip", "opening", "friendly"], DurationMs = 4800,
                },
            ],
        };
        var settingsHost = new FakeSettingsHost();
        var board = new BoardViewModel(host, host, host, host, settingsHost,
            new StatusViewModel(host), new SettingsViewModel(settingsHost));

        double? plainHeight = null;
        double? busyHeight = null;

        app.Dispatcher.Invoke(() =>
        {
            App.ApplyTheme(app.Theme);
            var window = new MainWindow { DataContext = board, Width = 1366, Height = 780 };
            window.Show();
            window.UpdateLayout();

            plainHeight = TileFillHeight(window, board.Phrases.Single(p => p.Entry.Id == "no-tags"));
            busyHeight = TileFillHeight(window, board.Phrases.Single(p => p.Entry.Id == "has-tags"));

            window.Close();
        });

        Assert.NotNull(plainHeight);
        Assert.NotNull(busyHeight);
        Assert.True(Math.Abs(128.0 - plainHeight!.Value) < 0.5,
            $"No-tag tile's visible fill should be 128px tall, was {plainHeight}.");
        Assert.True(Math.Abs(128.0 - busyHeight!.Value) < 0.5,
            $"Long-title/multi-tag tile's visible fill should still be 128px tall, was {busyHeight}.");
    }

    /// <summary>The height of the tile's visible painted surface (PhraseTileFillStyle's Border) — not
    /// just the Button's own hit-box, which is fixed by its Style regardless of this bug.</summary>
    private static double? TileFillHeight(DependencyObject root, PhraseItemViewModel item)
    {
        var button = FindButtonFor(root, item);
        return button?.Content is Grid { Children.Count: > 0 } contentGrid
            ? (contentGrid.Children[0] as FrameworkElement)?.ActualHeight
            : null;
    }

    private static Button? FindButtonFor(DependencyObject root, PhraseItemViewModel item)
    {
        if (root is Button b && ReferenceEquals(b.CommandParameter, item))
            return b;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindButtonFor(VisualTreeHelper.GetChild(root, i), item);
            if (found is not null)
                return found;
        }
        return null;
    }
}
