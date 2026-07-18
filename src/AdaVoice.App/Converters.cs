using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App;

/// <summary>Shared frozen-brush helper for converters below — a frozen brush is cross-thread safe and
/// slightly cheaper to render, and every converter here needs the same construction.</summary>
internal static class BrushHelpers
{
    public static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Hex string (e.g. "#54D262") → a <see cref="SolidColorBrush"/>. Empty or invalid input falls
/// back to the current theme's Surface.Raised, so an uncategorised or colourless phrase tile still
/// looks normal in both themes (a frozen literal here would go stale every time the palette changes,
/// as the old #2B2B2B fallback did).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && TryParse(hex, out var color))
            return BrushHelpers.Frozen(color);
        return Application.Current.Resources["Surface.Raised"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>Phrase title → clamped to the tile's 2-line budget with a trailing "…" (09's "2-line
/// clamp + ellipsis, full title in tooltip"). WPF's TextBlock has no multi-line-aware trimming: with
/// TextWrapping="Wrap", TextTrimming="CharacterEllipsis" collapses to single-line-with-ellipsis the
/// moment the full wrapped text doesn't fit the given height, instead of showing 2 full lines then
/// trimming the second (confirmed 2026-07-18 by rendering the same text at several MaxHeight values).
/// This truncates the string itself so plain Wrap (no TextTrimming) renders it — measured with a
/// real, never-shown <see cref="TextBlock"/> rather than <see cref="FormattedText"/>: the two
/// disagreed on where a line broke for the same string/width/font in testing, so only the actual
/// rendering control's own layout pass can be trusted to match what MainWindow later renders.</summary>
public sealed class TitleClampConverter : IValueConverter
{
    // The tile's REAL available width for the title, not its XAML MaxWidth (130, which never
    // actually binds — the parent geometry constrains it tighter first: 148 tile width - 20
    // Padding - 5 ribbon column - 10 title-Grid Margin = 113 on paper). A live tile's title
    // TextBlock.ActualWidth measured 110.4, not 113 — close, but a longer title with different
    // word-break points wrapped one line further in the real control than this measured, again
    // silently clipping the tail. 108 (not 110.4) is a small deliberate safety margin below the
    // measured value: measuring narrower than reality only wraps MORE eagerly (a slightly shorter
    // clamp than strictly necessary), never less — the failure mode this guards against (measuring
    // WIDER than reality) is the one that silently clips text.
    private const double MaxWidth = 108;
    private const double MaxHeightForTwoLines = 44;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? "";
        if (text.Length == 0)
            return text;

        if (Measure(text) <= MaxHeightForTwoLines)
            return text;

        // Longest prefix (+ "…") whose wrapped height still fits the 2-line budget.
        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Measure(text[..mid].TrimEnd() + "…") <= MaxHeightForTwoLines)
                lo = mid;
            else
                hi = mid - 1;
        }
        return text[..lo].TrimEnd() + "…";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static double Measure(string text)
    {
        var probe = new TextBlock
        {
            Text = text,
            FontSize = (double)Application.Current.Resources["FontSize.SectionTitle"],
            FontWeight = (FontWeight)Application.Current.Resources["Weight.Strong"],
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
            TextWrapping = TextWrapping.Wrap,
            Width = MaxWidth,
        };
        probe.Measure(new Size(MaxWidth, double.PositiveInfinity));
        return probe.DesiredSize.Height;
    }
}

/// <summary>An environment check's pass/fail status → a "✓ Pass"/"✗ Fail" label.</summary>
public sealed class CheckStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CheckStatus.Pass ? "✓ Pass" : "✗ Fail";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The whole bound <see cref="EnvironmentCheck"/> (via a path-less `{Binding}`) →
/// visible only for the failed cable-output check, so the VB-CABLE download link shows next to
/// that one check's detail text and nowhere else. Matches by <see cref="EnvironmentCheck.Name"/>
/// since no `CheckType` enum exists — see EnvironmentChecks.cs's "Cable output" literal.</summary>
public sealed class FailedCableCheckToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is EnvironmentCheck { Name: "Cable output", Status: CheckStatus.Fail }
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
