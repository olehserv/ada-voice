using NAudio.Wave;

namespace AdaVoice.Spike;

/// <summary>
/// Volume stage with a linear gain ramp (default 50 ms) so duck transitions
/// don't click on the live voice. Spike-grade equivalent of the production
/// mic duck stage (design 06 §1).
/// </summary>
public class RampGain : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float _current = 1f;
    private float _target = 1f;
    private float _stepPerSample;

    public RampGain(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public void SetTarget(float linearGain, int rampMs)
    {
        _target = linearGain;
        var rampSamples = Math.Max(1, WaveFormat.SampleRate * rampMs / 1000);
        _stepPerSample = Math.Abs(_target - _current) / rampSamples;
    }

    public static float DbToLinear(double db) => (float)Math.Pow(10, db / 20.0);

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        for (var i = 0; i < read; i++)
        {
            if (_current < _target)
                _current = Math.Min(_target, _current + _stepPerSample);
            else if (_current > _target)
                _current = Math.Max(_target, _current - _stepPerSample);
            buffer[offset + i] *= _current;
        }
        return read;
    }
}
