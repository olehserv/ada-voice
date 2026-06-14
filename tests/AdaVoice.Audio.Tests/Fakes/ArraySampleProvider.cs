using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Fakes;

/// <summary>
/// A simple read-only audio source over a float array. Tests use it to feed known
/// samples into a mixer or a render device. It returns 0 when the array is finished.
/// </summary>
public sealed class ArraySampleProvider(float[] samples, WaveFormat format) : ISampleProvider
{
    private int _position;

    public WaveFormat WaveFormat { get; } = format;

    /// <summary>A source in the engine format (48 kHz, float, mono).</summary>
    public static ArraySampleProvider Mono48k(float[] samples) => new(samples, TestAudio.EngineFormat);

    public int Read(float[] buffer, int offset, int count)
    {
        var remaining = samples.Length - _position;
        var n = Math.Min(count, remaining);
        Array.Copy(samples, _position, buffer, offset, n);
        _position += n;
        return n;
    }
}
