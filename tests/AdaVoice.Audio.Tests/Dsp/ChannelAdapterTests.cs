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

    // The alarm device is the system default output, which can be a 5.1/7.1 surround device.
    // Mono must up-mix to any channel count, or those machines get no audible alarm, ever.
    [Fact]
    public void Mono_is_upmixed_to_surround_by_replicating_the_channel()
    {
        var surround = ChannelAdapter.Match(ArraySampleProvider.Mono48k([0.5f, -0.3f]), targetChannels: 6);

        Assert.Equal(6, surround.WaveFormat.Channels);
        var buffer = new float[12];
        var read = surround.Read(buffer, 0, 12);

        Assert.Equal(12, read);
        Assert.All(buffer[..6], s => Assert.Equal(0.5f, s));
        Assert.All(buffer[6..], s => Assert.Equal(-0.3f, s));
    }

    [Fact]
    public void Unsupported_conversion_throws()
    {
        var stereoSource = new ArraySampleProvider([0f, 0f], WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2));

        Assert.Throws<NotSupportedException>(() => ChannelAdapter.Match(stereoSource, targetChannels: 1));
    }
}
