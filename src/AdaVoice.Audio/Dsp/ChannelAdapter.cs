using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Audio.Dsp;

/// <summary>
/// Matches the channel count of a source to what an output device expects. The engine
/// mixes in mono. A cable or speaker may expect stereo, so we up-mix mono to stereo, but
/// only when the device needs it (design 06 §1).
/// </summary>
public static class ChannelAdapter
{
    public static ISampleProvider Match(ISampleProvider source, int targetChannels)
    {
        if (source.WaveFormat.Channels == targetChannels)
            return source;

        if (source.WaveFormat.Channels == 1 && targetChannels == 2)
            return new MonoToStereoSampleProvider(source);

        throw new NotSupportedException(
            $"Cannot adapt {source.WaveFormat.Channels} channels to {targetChannels}.");
    }
}
