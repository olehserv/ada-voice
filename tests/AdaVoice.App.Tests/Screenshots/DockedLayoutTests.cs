using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests.Screenshots;

/// <summary>
/// Regression coverage for Slice 3 (Full/Docked responsive layout, resolved 2026-07-20 — no
/// category rail; one continuous layout at every width). Structural-fix-plan Pass 3 (audit C1)
/// verified the search/filter row and phrase WrapPanel hold up at a typical 1366px desktop width,
/// but never at the enforced <c>MinWidth="420"</c> — the tightest point of the primary Docked
/// shape (a narrow strip beside full-screen Chrome). This closes that gap: the filter row's four
/// controls (search box, Category filter, Conversation filter, Record) must not clip or overlap
/// at 420px.
///
/// Plain layout assertions, not screenshots, but still need a real shown Window for Measure/Arrange
/// to run — reuses the same "needs an interactive desktop" gate as the screenshot tests (see
/// <see cref="ScreenshotFactAttribute"/>) rather than inventing a separate one.
/// </summary>
[Collection(WpfAppCollection.Name)]
[Trait("Category", "Screenshot")]
public sealed class DockedLayoutTests(WpfAppFixture app)
{
    [ScreenshotFact]
    public void Filter_row_controls_fit_without_clipping_at_the_docked_minimum_width()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Short", CategoryId = Category.DefaultId, DurationMs = 1000 }],
        };
        var settingsHost = new FakeSettingsHost();
        var board = new BoardViewModel(host, host, host, host, settingsHost,
            new StatusViewModel(host), new SettingsViewModel(settingsHost));

        double? searchWidth = null;
        double? categoryWidth = null;
        double? conversationWidth = null;
        double? recordWidth = null;
        double? rowWidth = null;

        app.Dispatcher.Invoke(() =>
        {
            App.ApplyTheme(app.Theme);
            var window = new MainWindow { DataContext = board, Width = 420, Height = 560 };
            window.Show();
            window.UpdateLayout();

            var searchBox = (FrameworkElement)window.FindName("SearchBox")!;
            var categoryButton = FindByAutomationName(window, "Category filter");
            var conversationButton = FindByAutomationName(window, "Conversation filter");
            var recordButton = FindByContent(window, "Record");

            searchWidth = searchBox.ActualWidth;
            categoryWidth = categoryButton?.ActualWidth;
            conversationWidth = conversationButton?.ActualWidth;
            recordWidth = recordButton?.ActualWidth;
            // The filter-buttons row (Category / Conversation / spacer / Record) is the row whose
            // combined content is most likely to overflow a 420px window — measure its own width,
            // not the window's, since margins/scrollbar reduce what's actually available.
            rowWidth = ((FrameworkElement)categoryButton!.Parent!).ActualWidth;

            window.Close();
        });

        Assert.True(searchWidth > 0, "Search box should have a positive rendered width.");
        Assert.True(categoryWidth > 0, "Category filter button should have a positive rendered width.");
        Assert.True(conversationWidth > 0, "Conversation filter button should have a positive rendered width.");
        Assert.True(recordWidth > 0, "Record button should have a positive rendered width.");

        // Category + Conversation + Record must fit within the row's own rendered width — if they
        // didn't, WPF would still report positive individual widths (each control lays out fine on
        // its own) while visually overflowing the 420px window, which is exactly the class of bug a
        // width-only assertion above would miss.
        var combined = categoryWidth!.Value + conversationWidth!.Value + recordWidth!.Value;
        Assert.True(combined <= rowWidth!.Value + 0.5,
            $"Category ({categoryWidth}) + Conversation ({conversationWidth}) + Record ({recordWidth}) " +
            $"= {combined} should fit within the filter row's width ({rowWidth}) at the 420px docked minimum.");
    }

    private static FrameworkElement? FindByAutomationName(DependencyObject root, string name)
    {
        if (root is FrameworkElement { } fe && AutomationProperties.GetName(fe) == name)
            return fe;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindByAutomationName(VisualTreeHelper.GetChild(root, i), name);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static FrameworkElement? FindByContent(DependencyObject root, string content)
    {
        if (root is ContentControl { Content: string s } cc && s == content)
            return cc;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindByContent(VisualTreeHelper.GetChild(root, i), content);
            if (found is not null)
                return found;
        }
        return null;
    }
}
