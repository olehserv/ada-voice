namespace AdaVoice.App.Tests;

public class ColorContrastTests
{
    [Theory]
    [InlineData("#FFFFFF", true)]  // white background → dark text
    [InlineData("#000000", false)] // black background → white text
    [InlineData("#FFD43B", true)]  // bright yellow → dark text
    [InlineData("#4CC2FF", true)]  // light accent blue → dark text
    [InlineData("#7A3E9D", false)] // deep purple → white text
    public void Picks_dark_text_on_light_colours_and_white_on_dark(string hex, bool prefersDark)
    {
        Assert.Equal(prefersDark, ColorContrast.PrefersDarkText(hex));
    }

    // The crossover is where the WCAG math earns its keep: #777 is just light enough for dark text,
    // #666 just dark enough for white text.
    [Fact]
    public void Handles_the_mid_tone_crossover_both_ways()
    {
        Assert.True(ColorContrast.PrefersDarkText("#777777"));
        Assert.False(ColorContrast.PrefersDarkText("#666666"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-colour")]
    [InlineData("#12")]
    public void Unparseable_input_falls_back_to_light_text(string? hex)
    {
        Assert.False(ColorContrast.PrefersDarkText(hex)); // false = white text on the dark board
    }

    [Fact]
    public void Accepts_hex_without_a_leading_hash()
    {
        Assert.True(ColorContrast.PrefersDarkText("FFFFFF"));
    }
}
