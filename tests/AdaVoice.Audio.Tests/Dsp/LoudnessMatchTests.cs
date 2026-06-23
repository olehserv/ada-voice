using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests.Dsp;

public class LoudnessMatchTests
{
    private const double ReferenceDbfs = -20;
    private static readonly double ReferenceRms = RampGain.DbToLinear(ReferenceDbfs);

    [Theory]
    [InlineData(0.1)]   // quiet take -> needs boost
    [InlineData(0.5)]   // loud take  -> needs attenuation
    public void Matched_signal_hits_the_reference_rms_and_stays_under_the_ceiling(double amplitude)
    {
        var take = TestAudio.Sine(440, 48_000, amplitude);

        var gained = ApplyGain(take, LoudnessMatch.ComputeGainDb(take, ReferenceRms));

        Assert.Equal(ReferenceDbfs, Dbfs(Loudness.Rms(gained)), precision: 1); // within ~0.5 dB
        Assert.True(Dbfs(Loudness.Peak(gained)) <= -3 + 1e-6, "peak must stay under -3 dBFS");
    }

    [Fact]
    public void Peak_ceiling_wins_over_the_rms_target_for_a_high_crest_take()
    {
        // Mostly quiet with one full-scale spike: low RMS (wants big boost) but peak is already 1.0.
        var take = Enumerable.Repeat(0.01f, 1000).ToArray();
        take[500] = 1.0f;

        var gained = ApplyGain(take, LoudnessMatch.ComputeGainDb(take, ReferenceRms));

        Assert.True(Dbfs(Loudness.Peak(gained)) <= -3 + 1e-6, "peak ceiling must hold");
        Assert.True(Dbfs(Loudness.Rms(gained)) < ReferenceDbfs, "RMS yields to the ceiling here");
    }

    [Fact]
    public void Silent_take_gets_no_gain()
    {
        Assert.Equal(0, LoudnessMatch.ComputeGainDb(new float[1000], ReferenceRms));
    }

    private static float[] ApplyGain(float[] samples, double gainDb)
    {
        var g = RampGain.DbToLinear(gainDb);
        return samples.Select(s => s * g).ToArray();
    }

    private static double Dbfs(double linear) => 20 * Math.Log10(linear);
}
