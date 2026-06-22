using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests.Dsp;

public class LoudnessTests
{
    [Fact]
    public void Rms_of_a_constant_signal_is_its_magnitude()
    {
        var samples = Enumerable.Repeat(0.5f, 1000).ToArray();
        Assert.Equal(0.5, Loudness.Rms(samples), precision: 3);
    }

    [Fact]
    public void Rms_of_a_sine_is_amplitude_over_root_two()
    {
        var samples = TestAudio.Sine(440, 48_000, amplitude: 0.8);
        Assert.Equal(0.8 / Math.Sqrt(2), Loudness.Rms(samples), precision: 2);
    }

    [Fact]
    public void Peak_is_the_largest_absolute_sample()
    {
        var samples = TestAudio.Sine(440, 48_000, amplitude: 0.8);
        Assert.Equal(0.8, Loudness.Peak(samples), precision: 2);
    }

    [Fact]
    public void Empty_buffer_has_zero_rms_and_peak()
    {
        Assert.Equal(0, Loudness.Rms([]));
        Assert.Equal(0, Loudness.Peak([]));
    }
}
