namespace AdaVoice.Host;

/// <summary>
/// The saved size and position of the main window. A small value type on the settings seam so the WPF
/// window can restore itself on next launch. Plain doubles (no WPF <c>Rect</c>) keep this — and its
/// clamp logic — free of any UI dependency and unit-testable.
/// </summary>
public readonly record struct WindowPlacement(double Width, double Height, double Left, double Top)
{
    /// <summary>
    /// Fit this placement inside the given screen rectangle (the virtual desktop of all monitors).
    /// Guards against a saved position on a monitor that has since been unplugged: the size is capped to
    /// the screen, then the top-left is pulled back so the whole window — including its title bar — is
    /// reachable. Pure double math, so it needs no WPF and is easy to test.
    /// </summary>
    public WindowPlacement ClampTo(double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        var width = Math.Min(Width, screenWidth);
        var height = Math.Min(Height, screenHeight);
        var left = Math.Clamp(Left, screenLeft, screenLeft + screenWidth - width);
        var top = Math.Clamp(Top, screenTop, screenTop + screenHeight - height);
        return new WindowPlacement(width, height, left, top);
    }
}
