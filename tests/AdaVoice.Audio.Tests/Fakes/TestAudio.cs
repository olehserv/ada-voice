using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Fakes;

/// <summary>
/// Small helpers to build and read test audio. The engine format is 48 kHz, float, mono.
/// </summary>
public static class TestAudio
{
    public const int SampleRate = AudioFormats.SampleRate;

    /// <summary>The format the engine uses: 48 kHz, float, mono.</summary>
    public static WaveFormat EngineFormat => AudioFormats.Engine;

    /// <summary>Turn float samples into raw IEEE-float bytes.</summary>
    public static byte[] ToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>Read raw IEEE-float bytes back into float samples.</summary>
    public static float[] ToFloats(byte[] bytes, int byteCount)
    {
        var samples = new float[byteCount / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(float));
        return samples;
    }

    /// <summary>Make a sine wave. Useful as a known test signal.</summary>
    public static float[] Sine(double freqHz, int sampleCount, double amplitude = 0.5, int sampleRate = SampleRate)
    {
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * freqHz * i / sampleRate));
        return samples;
    }
}
