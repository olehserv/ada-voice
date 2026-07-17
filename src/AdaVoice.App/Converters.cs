using System.Globalization;
using System.Windows;
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
/// back to the raised-surface colour, so an uncategorised or colourless phrase tile still looks normal.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    // Matches Theme/Tokens.xaml Surface.Raised — the neutral tile fill when no category colour applies.
    private static readonly SolidColorBrush Fallback = BrushHelpers.Frozen(Color.FromRgb(0x2B, 0x2B, 0x2B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && TryParse(hex, out var color))
            return BrushHelpers.Frozen(color);
        return Fallback;
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

/// <summary>Hex background colour → black or white text brush, whichever reads better (WCAG contrast).
/// Drives every foreground mark on a filled phrase tile so nothing goes illegible on a saturated fill.</summary>
public sealed class ContrastTextConverter : IValueConverter
{
    private static readonly SolidColorBrush Dark = BrushHelpers.Frozen(Colors.Black);
    private static readonly SolidColorBrush Light = BrushHelpers.Frozen(Colors.White);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ColorContrast.PrefersDarkText(value as string) ? Dark : Light;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>An environment check's pass/fail status → a "✓ Pass"/"✗ Fail" label.</summary>
public sealed class CheckStatusToLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CheckStatus.Pass ? "✓ Pass" : "✗ Fail";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>An environment check's pass/fail status → a green/red brush.</summary>
public sealed class CheckStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Pass = BrushHelpers.Frozen(Color.FromRgb(0x54, 0xD2, 0x62)); // Status.Live
    private static readonly SolidColorBrush Fail = BrushHelpers.Frozen(Color.FromRgb(0xFF, 0x6B, 0x6B)); // Status.Degraded

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is CheckStatus.Pass ? Pass : Fail;

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
