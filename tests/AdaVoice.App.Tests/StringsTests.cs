using System.Globalization;
using AdaVoice.App.Resources;

namespace AdaVoice.App.Tests;

/// <summary>
/// Proves the localization pipeline end to end: Strings.resx (English) plus its uk/pl
/// satellites resolve correctly through the hand-written <see cref="Strings"/> accessor, keyed
/// off <see cref="CultureInfo.CurrentUICulture"/> — the same mechanism App.xaml.cs uses at
/// startup. Restores the ambient culture after each test so this doesn't leak into other tests
/// sharing the process.
/// </summary>
public class StringsTests
{
    [Fact]
    public void Resolves_English_by_default()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-US");

            Assert.Equal("Record", Strings.Main_Record);
            Assert.Equal("STOP", Strings.Main_Stop);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Resolves_Ukrainian_from_the_satellite_resource()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("uk-UA");

            Assert.Equal("Записати", Strings.Main_Record);
            Assert.Equal("СТОП", Strings.Main_Stop);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Resolves_Polish_from_the_satellite_resource()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("pl-PL");

            Assert.Equal("Nagraj", Strings.Main_Record);
            Assert.Equal("STOP", Strings.Main_Stop);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }
}
