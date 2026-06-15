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

        DriftKind? lastDrift = null;
        passthrough.Drift += (_, e) => lastDrift = e.Kind;

        capture.Start();

        // Nobody reads the output, so the backlog grows past the 100 ms limit.
        capture.PumpMilliseconds(60); // backlog ~60 ms
        capture.PumpMilliseconds(60); // backlog ~120 ms
        capture.PumpMilliseconds(60); // sees >100 ms, drops oldest, counts one overrun

        Assert.Equal(1, passthrough.Overruns);
        Assert.Equal(DriftKind.Overrun, lastDrift); // surfaced as an event, not just a counter
    }

    [Fact]
    public void Underrun_keeps_the_mic_in_the_mixer_and_emits_silence()
    {
        // Cardinal rule (06 §2): the live mic must never silently disappear. NAudio's mixer
        // removes any input whose Read returns fewer samples than asked. So when the buffer
        // runs empty, the passthrough must return silence (full count), not a short read.
        // This guards the BufferedWaveProvider.ReadFully setting against a future regression.
        var mic = Enumerable.Repeat(0.2f, 480).ToArray(); // only 10 ms of audio
        var capture = FileCaptureDevice.FromFloat(mic);
        using var passthrough = new MicPassthrough(capture);

        DriftKind? lastDrift = null;
        passthrough.Drift += (_, e) => lastDrift = e.Kind;

        var mixer = new MixingSampleProvider(TestAudio.EngineFormat) { ReadFully = true };
        mixer.AddMixerInput(passthrough.Output);

        capture.Start();
        while (capture.PumpMilliseconds(20)) { }

        // Ask for 100 ms — far more than the 10 ms we have — so the buffer underruns.
        var buffer = new float[4800];
        var read = mixer.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);                      // full buffer returned, no short read
        Assert.Contains(passthrough.Output, mixer.MixerInputs); // mic input was NOT dropped
        Assert.Equal(0f, buffer[^1]);                           // the underrun tail is silence
        Assert.Equal(1, passthrough.Underruns);                 // counted, not silent
        Assert.Equal(DriftKind.Underrun, lastDrift);            // and surfaced as an event
    }

    private static void AssertClose(float[] expected, IReadOnlyList<float> actual, float tolerance = 1e-6f)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance,
                $"sample {i}: expected {expected[i]}, got {actual[i]}");
    }
}
