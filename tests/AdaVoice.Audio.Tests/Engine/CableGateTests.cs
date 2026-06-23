using AdaVoice.Audio;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Tests.Engine.Fakes;
using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests.Engine;

public class CableGateTests
{
    [Fact]
    public void Open_gate_passes_samples_through()
    {
        var clock = new ManualEngineClock();
        var gate = new CableGate(ArraySampleProvider.Mono48k([0.5f, -0.5f, 1f]), clock);

        var buffer = new float[3];
        var read = gate.Read(buffer, 0, 3);

        Assert.Equal(3, read);
        Assert.Equal([0.5f, -0.5f, 1f], buffer);
    }

    [Fact]
    public void Closed_gate_outputs_silence_but_still_pulls()
    {
        var clock = new ManualEngineClock();
        var source = ArraySampleProvider.Mono48k([0.5f, -0.5f, 1f]);
        var gate = new CableGate(source, clock) { IsOpen = false };

        var buffer = new float[3];
        var read = gate.Read(buffer, 0, 3);

        Assert.Equal(3, read);               // still pulled the source (drains the mic buffer)
        Assert.Equal([0f, 0f, 0f], buffer);  // but emitted silence
    }

    [Fact]
    public void LastReadMs_is_seeded_from_the_clock_at_construction()
    {
        // The watchdog consults LastReadMs on a freshly built gate; it must not read 0
        // (which would look like a stall before the first render pull).
        var clock = new ManualEngineClock { NowMs = 999 };
        var gate = new CableGate(ArraySampleProvider.Mono48k([]), clock);

        Assert.Equal(999, gate.LastReadMs);
    }

    [Fact]
    public void Read_stamps_the_last_read_time_from_the_clock()
    {
        var clock = new ManualEngineClock { NowMs = 1234 };
        var gate = new CableGate(ArraySampleProvider.Mono48k([0f, 0f]), clock);

        gate.Read(new float[2], 0, 2);

        Assert.Equal(1234, gate.LastReadMs);
    }
}
