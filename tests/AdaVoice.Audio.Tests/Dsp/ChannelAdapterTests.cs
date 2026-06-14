using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Dsp;

public class ChannelAdapterTests
{
    [Fact]
    public void Same_channel_count_returns_the_same_source()
    {
        var source = ArraySampleProvider.Mono48k([0.5f]);

        Assert.Same(source, ChannelAdapter.Match(source, targetChannels: 1));
    }

    [Fact]
    public void Mono_is_upmixed_to_stereo_by_copying_the_channel()
    {
        var stereo = ChannelAdapter.Match(ArraySampleProvider.Mono48k([0.5f, -0.3f]), targetChannels: 2);

        Assert.Equal(2, stereo.WaveFormat.Channels);
        var buffer = new float[4];
        var read = stereo.Read(buffer, 0, 4);

        Assert.Equal(4, read);
        Assert.Equal([0.5f, 0.5f, -0.3f, -0.3f], buffer);
    }

    [Fact]
    public void Unsupported_conversion_throws()
    {
        var stereoSource = new ArraySampleProvider([0f, 0f], WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));

        Assert.Throws<NotSupportedException>(() => ChannelAdapter.Match(stereoSource, targetChannels: 1));
    }
}
