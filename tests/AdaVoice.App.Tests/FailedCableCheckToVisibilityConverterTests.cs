using System.Globalization;
using System.Windows;
using AdaVoice.App;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class FailedCableCheckToVisibilityConverterTests
{
    private static readonly FailedCableCheckToVisibilityConverter Sut = new();

    [Fact]
    public void Visible_when_the_cable_check_failed()
    {
        var check = new EnvironmentCheck("Cable output", CheckStatus.Fail, "not found");

        var result = Sut.Convert(check, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Collapsed_when_the_cable_check_passed()
    {
        var check = new EnvironmentCheck("Cable output", CheckStatus.Pass, "ok");

        var result = Sut.Convert(check, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Collapsed_when_a_different_check_fails()
    {
        var check = new EnvironmentCheck("Default output", CheckStatus.Fail, "is the cable");

        var result = Sut.Convert(check, typeof(Visibility), null, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ConvertBack_is_not_supported()
    {
        Assert.Throws<NotSupportedException>(() =>
            Sut.ConvertBack(Visibility.Visible, typeof(EnvironmentCheck), null, CultureInfo.InvariantCulture));
    }
}
