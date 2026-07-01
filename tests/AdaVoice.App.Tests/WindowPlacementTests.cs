using AdaVoice.Host;

namespace AdaVoice.App.Tests;

public class WindowPlacementTests
{
    // A single 1920x1080 monitor at the origin.
    private const double ScreenLeft = 0, ScreenTop = 0, ScreenWidth = 1920, ScreenHeight = 1080;

    private static WindowPlacement Clamp(WindowPlacement p) =>
        p.ClampTo(ScreenLeft, ScreenTop, ScreenWidth, ScreenHeight);

    [Fact]
    public void A_placement_fully_on_screen_is_unchanged()
    {
        var p = new WindowPlacement(Width: 480, Height: 640, Left: 100, Top: 80);

        Assert.Equal(p, Clamp(p));
    }

    [Fact]
    public void A_position_on_an_unplugged_monitor_is_pulled_back_on_screen()
    {
        // Saved on a second monitor to the right that is no longer present.
        var clamped = Clamp(new WindowPlacement(480, 640, Left: 3000, Top: 200));

        // The whole window stays within the screen: right/bottom edges are inside.
        Assert.True(clamped.Left >= ScreenLeft);
        Assert.True(clamped.Left + clamped.Width <= ScreenWidth);
        Assert.True(clamped.Top + clamped.Height <= ScreenHeight);
    }

    [Fact]
    public void A_negative_position_is_clamped_to_the_top_left_corner()
    {
        var clamped = Clamp(new WindowPlacement(480, 640, Left: -500, Top: -300));

        Assert.Equal(ScreenLeft, clamped.Left);
        Assert.Equal(ScreenTop, clamped.Top);
    }

    [Fact]
    public void A_window_larger_than_the_screen_is_capped_to_the_screen()
    {
        var clamped = Clamp(new WindowPlacement(Width: 4000, Height: 3000, Left: 0, Top: 0));

        Assert.Equal(ScreenWidth, clamped.Width);
        Assert.Equal(ScreenHeight, clamped.Height);
    }

    [Fact]
    public void The_title_bar_stays_reachable_for_any_saved_position()
    {
        // Even from a wildly off-screen origin, the top-left lands inside the screen.
        var clamped = Clamp(new WindowPlacement(480, 640, Left: 99999, Top: 99999));

        Assert.InRange(clamped.Left, ScreenLeft, ScreenLeft + ScreenWidth);
        Assert.InRange(clamped.Top, ScreenTop, ScreenTop + ScreenHeight);
    }
}
