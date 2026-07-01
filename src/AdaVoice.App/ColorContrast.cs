namespace AdaVoice.App;

/// <summary>
/// Picks readable text (black vs white) for a filled background colour, using the WCAG relative-
/// luminance contrast rule. Pure presentation logic with no WPF dependency (it returns a bool), so it
/// is unit-testable without a UI. Lives in the App layer because nothing in the domain decides text
/// colour — only the phrase board does.
/// </summary>
public static class ColorContrast
{
    /// <summary>
    /// True when dark (black) text reads better than white text on <paramref name="hex"/> — i.e. the
    /// colour is light. Empty or unparseable input returns false (use light text), which matches the
    /// dark default board background.
    /// </summary>
    public static bool PrefersDarkText(string? hex)
    {
        if (!TryParseHex(hex, out var r, out var g, out var b))
            return false;

        var luminance = 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);

        // Contrast against black is (L+0.05)/0.05; against white is 1.05/(L+0.05). Dark text wins when
        // its ratio is the larger — algebraically when L > sqrt(1.05*0.05) - 0.05 ≈ 0.179.
        var contrastWithBlack = (luminance + 0.05) / 0.05;
        var contrastWithWhite = 1.05 / (luminance + 0.05);
        return contrastWithBlack >= contrastWithWhite;
    }

    // sRGB channel (0..1) to linear light, per the WCAG definition.
    private static double Linearize(double channel) =>
        channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static bool TryParseHex(string? hex, out double r, out double g, out double b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6)
            return false;

        try
        {
            r = Convert.ToInt32(s.Substring(0, 2), 16) / 255.0;
            g = Convert.ToInt32(s.Substring(2, 2), 16) / 255.0;
            b = Convert.ToInt32(s.Substring(4, 2), 16) / 255.0;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
