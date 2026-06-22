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
public sealed class MicPassthrough : IMicDuck, IDisposable
{
    private const int MaxBacklogMs = 100;

    private readonly IAudioCaptureDevice _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly RampGain _gain;
    private long _overruns;
    private long _underruns;

    public MicPassthrough(IAudioCaptureDevice capture)
    {
        _capture = capture;
        _buffer = new BufferedWaveProvider(capture.Format)
        {
            BufferDuration = TimeSpan.FromMilliseconds(500),
            DiscardOnBufferOverflow = true,

            // Must stay true. The mixer drops any input whose Read returns fewer samples
            // than asked. On an underrun (buffer briefly empty) we must return silence to
            // fill the buffer, not a short read — otherwise the live mic input is removed
            // for good and her voice goes silent in the call (the cardinal rule, 06 §2).
            ReadFully = true,
        };
        _capture.DataAvailable += OnDataAvailable;

        var chain = EngineFormat.Convert(_buffer.ToSampleProvider());
        _gain = new RampGain(chain);

        // The watch sits at the very end of the chain so it sees the same reads the mixer makes.
        Output = new UnderrunWatch(_gain, _buffer, OnUnderrun);
    }

    /// <summary>The ducked mic signal in the engine format. The mixer reads this.</summary>
    public ISampleProvider Output { get; }

    /// <summary>The current mic gain (1 = full, lower = ducked). For tests and meters.</summary>
    public float CurrentMicGain => _gain.CurrentGain;

    /// <summary>How many times the backlog grew too large and old audio had to be dropped.</summary>
    public long Overruns => Interlocked.Read(ref _overruns);

    /// <summary>How many times the buffer ran dry and silence had to be inserted.</summary>
    public long Underruns => Interlocked.Read(ref _underruns);

    /// <summary>Raised on every drift event so the engine can log the cadence (design 06 §1).</summary>
    public event EventHandler<DriftEventArgs>? Drift;

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
            Drift?.Invoke(this, new DriftEventArgs(DriftKind.Overrun));
        }

        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnUnderrun()
    {
        Interlocked.Increment(ref _underruns);
        Drift?.Invoke(this, new DriftEventArgs(DriftKind.Underrun));
    }

    public void Dispose() => _capture.DataAvailable -= OnDataAvailable;

    /// <summary>
    /// Sits at the end of the mic chain and watches for underruns on the read side. Just before
    /// each read it checks whether the buffer holds enough audio for the request. If not, the
    /// buffer will pad the gap with silence (a brief audible gap), so we report it. The check is
    /// by time, so it is correct whether or not the chain resamples.
    /// </summary>
    private sealed class UnderrunWatch(ISampleProvider source, BufferedWaveProvider buffer, Action onUnderrun)
        : ISampleProvider
    {
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] output, int offset, int count)
        {
            var neededMs = count * 1000.0 / WaveFormat.SampleRate; // engine format is mono
            var haveMs = buffer.BufferedDuration.TotalMilliseconds;

            var read = source.Read(output, offset, count);

            if (haveMs < neededMs)
                onUnderrun();

            return read;
        }
    }
}
