using NAudio.Wave;

namespace AdaVoice.Audio.Playback;

/// <summary>
/// Plays one phrase as a mixer input. The phrase audio is already decoded to float
/// samples in the engine format. On <see cref="Stop"/> it applies a short linear
/// fade-out so the sound does not click, then it ends. When it ends, <see cref="Read"/>
/// returns fewer samples than asked (and then 0), which makes the mixer remove it.
/// </summary>
public sealed class PhraseSampleProvider : ISampleProvider
{
    private readonly float[] _data;
    private readonly Lock _sync = new();
    private int _position;
    private bool _stopping;
    private int _fadeSamplesLeft;
    private int _fadeSamplesTotal;
    private bool _finished;

    public PhraseSampleProvider(float[] data, WaveFormat format, string id)
    {
        _data = data;
        WaveFormat = format;
        Id = id;
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>Which phrase this is, so the player can track the active one.</summary>
    public string Id { get; }

    public bool IsFinished
    {
        get { lock (_sync) { return _finished; } }
    }

    /// <summary>Begin a linear fade-out over <paramref name="fadeMs"/>, then end.</summary>
    public void Stop(int fadeMs)
    {
        lock (_sync)
        {
            if (_finished || _stopping)
                return;

            _stopping = true;
            _fadeSamplesTotal = Math.Max(1, fadeMs * WaveFormat.SampleRate / 1000);
            _fadeSamplesLeft = _fadeSamplesTotal;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_sync)
        {
            if (_finished)
                return 0;

            var n = 0;
            while (n < count)
            {
                if (_position >= _data.Length)
                {
                    _finished = true; // played to the end
                    break;
                }

                var sample = _data[_position++];

                if (_stopping)
                {
                    if (_fadeSamplesLeft == 0)
                    {
                        _finished = true; // fade complete
                        break;
                    }

                    // Gain goes from 1 down to near 0 over the fade. Starting at 1 means
                    // there is no jump at the moment Stop is called.
                    sample *= (float)_fadeSamplesLeft / _fadeSamplesTotal;
                    _fadeSamplesLeft--;
                }

                buffer[offset + n] = sample;
                n++;
            }

            return n;
        }
    }
}
