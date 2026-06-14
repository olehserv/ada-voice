using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Passthrough;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Audio.Tests.Passthrough;

public class MicPassthroughTests
{
    [Fact]
    public void Live_path_sends_mic_audio_to_the_render_device()
    {
        var mic = TestAudio.Sine(440, sampleCount: 4800); // 100 ms at 48 kHz
        var capture = FileCaptureDevice.FromFloat(mic);
        using var passthrough = new MicPassthrough(capture);

        var mixer = new MixingSampleProvider(TestAudio.EngineFormat) { ReadFully = true };
        mixer.AddMixerInput(passthrough.Output);

        var render = MemoryRenderDevice.MonoFloat48k();
        render.Init(mixer);
        render.Start();

        capture.Start();
        while (capture.PumpMilliseconds(20)) { }
        render.Render(mic.Length);

        AssertClose(mic, render.Captured);
    }

    [Fact]
    public void Ducking_lowers_the_mic_gain_after_the_ramp()
    {
        var capture = FileCaptureDevice.FromFloat(Enumerable.Repeat(1f, 48_000).ToArray());
        using var passthrough = new MicPassthrough(capture);
        var render = MemoryRenderDevice.MonoFloat48k();
        render.Init(passthrough.Output);
        render.Start();
        capture.Start();
        while (capture.PumpMilliseconds(20)) { }

        passthrough.Duck(RampGain.DbToLinear(-12), rampMs: 10);
        render.Render(10 * TestAudio.SampleRate / 1000); // read the ramp window

        Assert.Equal(RampGain.DbToLinear(-12), passthrough.CurrentMicGain, 4);
    }

    [Fact]
    public void Backlog_over_the_limit_drops_oldest_audio_and_counts_an_overrun()
    {
        var capture = FileCaptureDevice.FromFloat(Enumerable.Repeat(0.1f, 48_000).ToArray());
        using var passthrough = new MicPassthrough(capture);
        capture.Start();

        // Nobody reads the output, so the backlog grows past the 100 ms limit.
        capture.PumpMilliseconds(60); // backlog ~60 ms
        capture.PumpMilliseconds(60); // backlog ~120 ms
        capture.PumpMilliseconds(60); // sees >100 ms, drops oldest, counts one overrun

        Assert.Equal(1, passthrough.Overruns);
    }

    private static void AssertClose(float[] expected, IReadOnlyList<float> actual, float tolerance = 1e-6f)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance,
                $"sample {i}: expected {expected[i]}, got {actual[i]}");
    }
}
