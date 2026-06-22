using AdaVoice.Audio;
using AdaVoice.Audio.Recording;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Recording;

public class RecorderTests
{
    [Fact]
    public void Records_an_engine_format_take_and_computes_a_matching_gain()
    {
        // 200 ms tone at 0.5 amplitude (~ -9 dBFS RMS); reference -20 dBFS -> gain ~ -11 dB.
        var tone = TestAudio.Sine(440, 48_000 / 5, amplitude: 0.5);
        var capture = FileCaptureDevice.FromFloat(tone);
        var recorder = new Recorder(capture);

        recorder.Start();
        while (capture.PumpMilliseconds(50)) { }
        var result = recorder.Stop();

        Assert.True(result.HasSignal);
        Assert.InRange(result.DurationMs, 200 - 60, 200 + 60); // tone + flush/padding slop
        Assert.True(result.GainDb < 0, "a loud take should be attenuated");
        Assert.True(Math.Abs(result.GainDb - (-11)) < 1.5, $"gain {result.GainDb} should be near -11 dB");
    }

    [Fact]
    public void Resamples_and_downmixes_a_441k_stereo_source_to_48k_mono()
    {
        // 500 ms of 44.1 kHz STEREO. A byte-copy (no convert) would yield ~924 ms; correct
        // downmix+resample yields ~500 ms. Duration is the discriminator that forces conversion.
        var capture = new FileCaptureDevice(StereoSineBytes(440, 44_100, channels: 2, ms: 500),
            WaveFormat.CreateIeeeFloatWaveFormat(44_100, 2));
        var recorder = new Recorder(capture);

        recorder.Start();
        while (capture.PumpMilliseconds(50)) { }
        var result = recorder.Stop();

        Assert.True(result.HasSignal);
        Assert.InRange(result.DurationMs, 500 - 60, 500 + 60);
    }

    [Fact]
    public void A_silent_take_reports_no_signal()
    {
        var capture = FileCaptureDevice.FromFloat(new float[48_000 / 3]); // 300 ms silence
        var recorder = new Recorder(capture);

        recorder.Start();
        while (capture.PumpMilliseconds(50)) { }
        var result = recorder.Stop();

        Assert.False(result.HasSignal);
    }

    private static byte[] StereoSineBytes(double freq, int rate, int channels, int ms)
    {
        var frames = rate * ms / 1000;
        var interleaved = new float[frames * channels];
        for (var i = 0; i < frames; i++)
        {
            var s = (float)(0.5 * Math.Sin(2 * Math.PI * freq * i / rate));
            for (var c = 0; c < channels; c++)
                interleaved[i * channels + c] = s;
        }

        var bytes = new byte[interleaved.Length * sizeof(float)];
        Buffer.BlockCopy(interleaved, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
