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
            throw new UnsupportedChannelCountException(source.WaveFormat.Channels);

        if (source.WaveFormat.SampleRate != AudioFormats.SampleRate)
            source = new WdlResamplingSampleProvider(source, AudioFormats.SampleRate);

        return source;
    }
}

/// <summary>A capture device delivered more than 2 channels. Carries the count only — Audio has no
/// display text; <see cref="AdaVoice.Audio.Engine.AudioEngine"/> maps this to
/// <see cref="AdaVoice.Audio.Engine.EngineErrorReason.TooManyMicChannels"/> for the App layer to
/// localize.</summary>
public sealed class UnsupportedChannelCountException(int channels) : Exception
{
    public int Channels { get; } = channels;
}
