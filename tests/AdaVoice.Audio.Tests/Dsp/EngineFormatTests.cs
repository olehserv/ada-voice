using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Dsp;

public class EngineFormatTests
{
    [Fact]
    public void Stereo_source_is_downmixed_to_mono()
    {
        var stereo = new ArraySampleProvider([0.5f, -0.3f], WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));

        var mono = EngineFormat.Convert(stereo);

        Assert.Equal(1, mono.WaveFormat.Channels);
    }

    [Fact]
    public void Source_with_more_than_two_channels_throws_with_the_channel_count()
    {
        var source = new ArraySampleProvider([0f, 0f, 0f, 0f], WaveFormat.CreateIeeeFloatWaveFormat(48_000, 4));

        var ex = Assert.Throws<UnsupportedChannelCountException>(() => EngineFormat.Convert(source));

        Assert.Equal(4, ex.Channels);
    }
}
