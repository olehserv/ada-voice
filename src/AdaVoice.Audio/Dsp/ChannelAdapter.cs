using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Audio.Dsp;

/// <summary>
/// Matches the channel count of a source to what an output device expects. The engine mixes
/// in mono; a device may expect stereo or even 6/8 channels (surround as the default output),
/// so mono up-mixes to any count. Down-mixing is still unsupported — nothing in the engine
/// produces multi-channel audio (design 06 §1).
/// </summary>
public static class ChannelAdapter
{
    public static ISampleProvider Match(ISampleProvider source, int targetChannels)
    {
        if (source.WaveFormat.Channels == targetChannels)
            return source;

        if (source.WaveFormat.Channels == 1 && targetChannels == 2)
            return new MonoToStereoSampleProvider(source);

        if (source.WaveFormat.Channels == 1 && targetChannels > 2)
            return new MonoToManySampleProvider(source, targetChannels);

        throw new NotSupportedException(
            $"Cannot adapt {source.WaveFormat.Channels} channels to {targetChannels}.");
    }
}
