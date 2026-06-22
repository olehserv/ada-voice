using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Audio.Dsp;

/// <summary>
/// Converts an arbitrary capture source to the engine format (48 kHz, mono): down-mixes stereo and
/// resamples as needed. Shared by <c>MicPassthrough</c> (pulled by the render thread) and the
/// recorder (pulled by a push-drain), so both treat real-device formats the same way.
/// </summary>
public static class EngineFormat
{
    public static ISampleProvider Convert(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 2)
            source = source.ToMono(0.5f, 0.5f); // average both channels to avoid clipping
        else if (source.WaveFormat.Channels > 2)
            throw new NotSupportedException(
                $"Source has {source.WaveFormat.Channels} channels. Use a mono or stereo device.");

        if (source.WaveFormat.SampleRate != AudioFormats.SampleRate)
            source = new WdlResamplingSampleProvider(source, AudioFormats.SampleRate);

        return source;
    }
}
