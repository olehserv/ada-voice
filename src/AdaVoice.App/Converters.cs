using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AdaVoice.App;

/// <summary>Hex string (e.g. "#54D262") → a <see cref="SolidColorBrush"/>. Empty or invalid input falls
/// back to the raised-surface colour, so an uncategorised or colourless phrase tile still looks normal.</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    // Matches Theme/Tokens.xaml Surface.Raised — the neutral tile fill when no category colour applies.
    private static readonly SolidColorBrush Fallback = Frozen(Color.FromRgb(0x2B, 0x2B, 0x2B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && TryParse(hex, out var color))
            return Frozen(color);
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

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Hex background colour → black or white text brush, whichever reads better (WCAG contrast).
/// Drives every foreground mark on a filled phrase tile so nothing goes illegible on a saturated fill.</summary>
public sealed class ContrastTextConverter : IValueConverter
{
    private static readonly SolidColorBrush Dark = Frozen(Colors.Black);
    private static readonly SolidColorBrush Light = Frozen(Colors.White);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ColorContrast.PrefersDarkText(value as string) ? Dark : Light;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

/// <summary>[current colour, swatch colour] → a white ring brush when they match, else transparent. Marks
/// the selected swatch in the colour picker.</summary>
public sealed class SwatchRingConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush Ring = Frozen(Colors.White);
    private static readonly SolidColorBrush None = Frozen(Colors.Transparent);

    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is string current && values[1] is string swatch
            && string.Equals(current.Trim(), swatch.Trim(), StringComparison.OrdinalIgnoreCase))
            return Ring;
        return None;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
