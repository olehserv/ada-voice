using AdaVoice.Audio.Setup;

namespace AdaVoice.Audio.Tests.Setup;

public class VoiceCalibrationTests
{
    [Fact]
    public void A_loud_enough_take_captures_the_reference()
    {
        var samples = Enumerable.Repeat(0.2f, 4_800).ToArray(); // RMS 0.2 (~ -14 dBFS), well above the floor

        var result = VoiceCalibration.FromTrimmedSamples(samples);

        Assert.True(result.Ok);
        Assert.Equal(0.2, result.MicReferenceRms, precision: 3);
    }

    [Fact]
    public void A_too_quiet_take_is_rejected_with_a_retry_message()
    {
        var samples = Enumerable.Repeat(0.0005f, 4_800).ToArray(); // ~ -66 dBFS, below the floor

        var result = VoiceCalibration.FromTrimmedSamples(samples);

        Assert.False(result.Ok);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public void An_empty_take_is_rejected()
    {
        Assert.False(VoiceCalibration.FromTrimmedSamples([]).Ok);
    }
}
