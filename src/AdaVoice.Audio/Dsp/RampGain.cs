using NAudio.Wave;

namespace AdaVoice.Audio.Dsp;

/// <summary>
/// Wraps an audio source and applies a gain (volume) that moves smoothly to a target
/// value over a short time. We use it to duck the live mic when a phrase plays: the gain
/// ramps down, then ramps back to full when the phrase ends. A smooth ramp avoids clicks.
/// </summary>
/// <remarks>
/// <see cref="SetTarget"/> is called by the engine control thread. <see cref="Read"/>
/// runs on the render thread. The lock keeps the ramp values consistent between the two.
/// The locked work is tiny (one audio buffer), so it is safe on the audio path here.
/// </remarks>
public sealed class RampGain(ISampleProvider source) : ISampleProvider
{
    private readonly Lock _sync = new();
    private float _current = 1f;
    private float _target = 1f;
    private float _step;
    private int _rampSamplesLeft;

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>The current gain. Mostly for tests and meters.</summary>
    public float CurrentGain
    {
        get { lock (_sync) { return _current; } }
    }

    /// <summary>Start moving the gain to <paramref name="target"/> over <paramref name="rampMs"/>.</summary>
    public void SetTarget(float target, int rampMs)
    {
        lock (_sync)
        {
            _target = target;
            var rampSamples = Math.Max(1, rampMs * WaveFormat.SampleRate / 1000);
            _rampSamplesLeft = rampSamples;
            _step = (_target - _current) / rampSamples;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var read = source.Read(buffer, offset, count);
        lock (_sync)
        {
            for (var i = 0; i < read; i++)
            {
                if (_rampSamplesLeft > 0)
                {
                    _current += _step;
                    if (--_rampSamplesLeft == 0)
                        _current = _target; // land exactly on the target
                }

                buffer[offset + i] *= _current;
            }
        }

        return read;
    }

    /// <summary>Convert a dB value to a linear gain. For example, -6 dB is about 0.5.</summary>
    public static float DbToLinear(double db) => (float)Math.Pow(10, db / 20);
}
