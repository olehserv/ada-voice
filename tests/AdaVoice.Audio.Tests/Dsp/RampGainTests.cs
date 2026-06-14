using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests.Dsp;

public class RampGainTests
{
    [Fact]
    public void Unity_gain_passes_samples_unchanged()
    {
        var gain = new RampGain(ArraySampleProvider.Mono48k([0.2f, -0.4f, 1f]));

        var buffer = new float[3];
        var read = gain.Read(buffer, 0, 3);

        Assert.Equal(3, read);
        Assert.Equal([0.2f, -0.4f, 1f], buffer);
        Assert.Equal(1f, gain.CurrentGain);
    }

    [Fact]
    public void Gain_reaches_the_target_after_the_ramp_window()
    {
        var ones = Enumerable.Repeat(1f, 1000).ToArray();
        var gain = new RampGain(ArraySampleProvider.Mono48k(ones));
        var rampSamples = 10 * TestAudio.SampleRate / 1000; // 10 ms = 480 samples

        gain.SetTarget(0.25f, rampMs: 10);
        var buffer = new float[rampSamples];
        gain.Read(buffer, 0, rampSamples);

        Assert.Equal(0.25, gain.CurrentGain, 5);   // landed on the target
        Assert.Equal(0.25, buffer[^1], 5);         // last sample fully ducked
        Assert.InRange(buffer[0], 0.25f, 1f);      // first sample still mid-ramp
    }

    [Fact]
    public void DbToLinear_matches_known_values()
    {
        Assert.Equal(1.0, RampGain.DbToLinear(0), 5);
        Assert.Equal(0.5, RampGain.DbToLinear(-6.0206), 4);
        Assert.Equal(0.25118864, RampGain.DbToLinear(-12), 5);
    }
}
