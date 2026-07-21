using NAudio.Wave;

namespace AdaVoice.Audio;

/// <summary>
/// The audio format used inside the engine: 48 kHz, 32-bit float, mono. Audio is
/// converted to this format at the input edge. It is converted again at the output edge
/// if a device needs a different layout, such as stereo (design 06 §1).
/// </summary>
public static class AudioFormats
{
    public const int SampleRate = 48_000;

    public static WaveFormat Engine { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
}

/// <summary>A render device (the cable or its monitor) is not delivering 48 kHz — the same condition
/// the setup wizard's environment check reports, hit live instead. No display text: Audio has none;
/// <see cref="AdaVoice.Audio.Engine.AudioEngine"/> maps this to
/// <see cref="AdaVoice.Audio.Engine.EngineErrorReason.CableSampleRateMismatch"/> for the App layer.</summary>
public sealed class UnsupportedSampleRateException : Exception;
