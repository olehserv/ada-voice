using AdaVoice.Audio.Dsp;

namespace AdaVoice.Audio.Tests.Dsp;

public class SilenceTrimTests
{
    // Use a 1 kHz "sample rate" so 150 ms padding = 150 samples, keeping the arithmetic simple.
    private const int Rate = 1000;

    [Fact]
    public void Trims_leading_and_trailing_silence_keeping_padding()
    {
        var clip = new float[1000]                       // 1000 silent
            .Concat(Enumerable.Repeat(0.5f, 100))        // 100 of signal (index 1000..1099)
            .Concat(new float[1000])                     // 1000 silent
            .ToArray();

        var trimmed = SilenceTrim.Trim(clip, Rate, thresholdDbfs: -45, paddingMs: 150);

        // 100 signal + 150 padding each side
        Assert.Equal(100 + 150 + 150, trimmed.Length);
        // The signal sits in the middle, after the leading padding.
        Assert.Equal(0.5f, trimmed[150]);
        Assert.Equal(0f, trimmed[0]);
    }

    [Fact]
    public void An_all_silent_take_returns_empty()
    {
        Assert.Empty(SilenceTrim.Trim(new float[5000], Rate));
    }

    [Fact]
    public void Padding_is_clamped_to_what_is_available()
    {
        var clip = Enumerable.Repeat(0.5f, 200).ToArray(); // no silence at all
        var trimmed = SilenceTrim.Trim(clip, Rate, paddingMs: 150);
        Assert.Equal(200, trimmed.Length);
    }
}
