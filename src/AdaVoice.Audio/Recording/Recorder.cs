using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Dsp;
using NAudio.Wave;

namespace AdaVoice.Audio.Recording;

/// <summary>
/// Records a single take from a capture device, converts it to the engine format (48 kHz mono),
/// then on <see cref="Stop"/> trims silence and computes the loudness-match gain. The take is used
/// while the engine is OFF AIR, so nothing reaches the call.
/// </summary>
/// <remarks>
/// Capture is push-based (the device calls us) but the format-conversion chain is pull-based, and
/// this recorder has no render thread to pull it. So we feed a <see cref="BufferedWaveProvider"/> on
/// each callback and immediately drain all currently-available converted samples. On stop we push a
/// little silence through to flush the resampler's internal latency, then drain — otherwise the tail
/// of the take is lost. The trailing silence is removed by the trim.
/// </remarks>
public sealed class Recorder
{
    private const int FlushMs = 20;

    private readonly IAudioCaptureDevice _capture;
    private readonly RecorderOptions _options;
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _converted;
    private readonly List<float> _samples = [];
    private readonly float[] _pull = new float[4096];

    public Recorder(IAudioCaptureDevice capture, RecorderOptions? options = null)
    {
        _capture = capture;
        _options = options ?? new RecorderOptions();

        _buffer = new BufferedWaveProvider(capture.Format)
        {
            // Drain stops when the source dries up; ReadFully would pad with silence forever.
            ReadFully = false,
            DiscardOnBufferOverflow = false, // we drain every callback, so it never fills
        };
        _converted = EngineFormat.Convert(_buffer.ToSampleProvider());
    }

    public void Start()
    {
        _samples.Clear();
        _capture.DataAvailable += OnDataAvailable;
        _capture.Start();
    }

    public RecordingResult Stop()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.Stop();

        // Flush the resampler's internal latency with a little silence, then drain. The trim below
        // removes this trailing silence.
        var flushBytes = _capture.Format.AverageBytesPerSecond * FlushMs / 1000;
        _buffer.AddSamples(new byte[flushBytes], 0, flushBytes);
        Drain();

        var trimmed = SilenceTrim.Trim(
            _samples.ToArray(), AudioFormats.SampleRate, _options.SilenceThresholdDbfs, _options.PaddingMs);
        if (trimmed.Length == 0)
            return RecordingResult.NoSignal;

        var referenceRms = RampGain.DbToLinear(_options.ReferenceDbfs);
        var gainDb = LoudnessMatch.ComputeGainDb(trimmed, referenceRms, _options.PeakCeilingDbfs);
        var durationMs = trimmed.Length * 1000 / AudioFormats.SampleRate;
        var peakDbfs = 20 * Math.Log10(Math.Max(Loudness.Peak(trimmed), 1e-9));

        return new RecordingResult(trimmed, gainDb, durationMs, peakDbfs);
    }

    private void OnDataAvailable(object? sender, CaptureBufferEventArgs e)
    {
        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        Drain();
    }

    private void Drain()
    {
        int n;
        while ((n = _converted.Read(_pull, 0, _pull.Length)) > 0)
            for (var i = 0; i < n; i++)
                _samples.Add(_pull[i]);
    }
}
