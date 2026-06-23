using NAudio.Wave;

namespace AdaVoice.Audio.Engine;

/// <summary>
/// An endless beeping tone for the DEGRADED alarm: 880 Hz on for 300 ms, off for 300 ms,
/// repeating. Loud and obviously wrong, so the operator cannot miss that the mic is down.
/// </summary>
public sealed class AlarmTone(WaveFormat format) : ISampleProvider
{
    private const double FreqHz = 880;
    private const float Amplitude = 0.6f;
    private long _n;

    public WaveFormat WaveFormat => format;

    public int Read(float[] buffer, int offset, int count)
    {
        var rate = WaveFormat.SampleRate;
        var halfCycle = rate * 3 / 10; // 300 ms in samples

        for (var i = 0; i < count; i++)
        {
            var onPhase = _n / halfCycle % 2 == 0;
            buffer[offset + i] = onPhase
                ? (float)(Amplitude * Math.Sin(2 * Math.PI * FreqHz * _n / rate))
                : 0f;
            _n++;
        }

        return count; // endless
    }
}
