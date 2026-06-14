using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Audio.Passthrough;

/// <summary>
/// Sends the live microphone into the engine. It takes raw audio from a capture device,
/// converts it to the engine format (48 kHz, float, mono), and applies the duck gain.
/// The result is exposed as <see cref="Output"/>, which the mixer reads.
/// </summary>
/// <remarks>
/// Capture is push-based: the device calls us when audio is ready. The mixer is
/// pull-based: it reads when it needs audio. A <see cref="BufferedWaveProvider"/> joins
/// the two sides and absorbs small clock differences between the mic and the output.
/// </remarks>
public sealed class MicPassthrough : IDisposable
{
    private const int MaxBacklogMs = 100;

    private readonly IAudioCaptureDevice _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly RampGain _gain;
    private long _overruns;

    public MicPassthrough(IAudioCaptureDevice capture)
    {
        _capture = capture;
        _buffer = new BufferedWaveProvider(capture.Format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true,
        };
        _capture.DataAvailable += OnDataAvailable;

        var chain = ToEngineFormat(_buffer.ToSampleProvider());
        _gain = new RampGain(chain);
        Output = _gain;
    }

    /// <summary>The ducked mic signal in the engine format. The mixer reads this.</summary>
    public ISampleProvider Output { get; }

    /// <summary>The current mic gain (1 = full, lower = ducked). For tests and meters.</summary>
    public float CurrentMicGain => _gain.CurrentGain;

    /// <summary>How many times the backlog grew too large and old audio had to be dropped.</summary>
    public long Overruns => Interlocked.Read(ref _overruns);

    /// <summary>Move the mic gain to <paramref name="targetGain"/> over <paramref name="rampMs"/>.</summary>
    public void Duck(float targetGain, int rampMs) => _gain.SetTarget(targetGain, rampMs);

    private void OnDataAvailable(object? sender, CaptureBufferEventArgs e)
    {
        // Drift policy (design 06 §1), overrun side: if the mic gets ahead of the output
        // and the backlog passes the limit, drop the oldest audio and count it. This is a
        // small skip in the live voice. The full policy (underrun and logging) lands with
        // the engine in a later slice.
        if (_buffer.BufferedDuration.TotalMilliseconds > MaxBacklogMs)
        {
            Interlocked.Increment(ref _overruns);
            _buffer.ClearBuffer();
        }

        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    private static ISampleProvider ToEngineFormat(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 2)
            source = source.ToMono(0.5f, 0.5f); // average both channels to avoid clipping
        else if (source.WaveFormat.Channels > 2)
            throw new NotSupportedException(
                $"Mic has {source.WaveFormat.Channels} channels. Use a mono or stereo device.");

        if (source.WaveFormat.SampleRate != AudioFormats.SampleRate)
            source = new WdlResamplingSampleProvider(source, AudioFormats.SampleRate);

        return source;
    }

    public void Dispose() => _capture.DataAvailable -= OnDataAvailable;
}
