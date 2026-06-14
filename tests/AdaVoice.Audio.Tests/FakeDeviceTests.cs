using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests;

/// <summary>
/// Sanity tests for the fake devices. The engine is built on top of these fakes, so they
/// must behave correctly before any engine code relies on them.
/// </summary>
public class FakeDeviceTests
{
    [Fact]
    public void TestAudio_converts_floats_to_bytes_and_back()
    {
        float[] samples = [0f, 0.25f, -0.5f, 1f, -1f];

        var bytes = TestAudio.ToBytes(samples);
        var roundTrip = TestAudio.ToFloats(bytes, bytes.Length);

        Assert.Equal(samples, roundTrip);
    }

    [Fact]
    public void FileCaptureDevice_delivers_all_data_then_reports_end()
    {
        var samples = TestAudio.Sine(440, 4800); // 100 ms at 48 kHz
        var device = FileCaptureDevice.FromFloat(samples);
        var received = new List<float>();
        device.DataAvailable += (_, e) => received.AddRange(TestAudio.ToFloats(e.Buffer, e.BytesRecorded));

        device.Start();
        bool more;
        do { more = device.PumpMilliseconds(20); } while (more);

        Assert.Equal(samples, received);
    }

    [Fact]
    public void FileCaptureDevice_pump_before_start_throws()
    {
        var device = FileCaptureDevice.FromFloat([0f, 1f]);

        Assert.Throws<InvalidOperationException>(() => device.Pump(8));
    }

    [Fact]
    public void MemoryRenderDevice_stores_samples_pulled_from_the_source()
    {
        float[] samples = [0.1f, 0.2f, 0.3f, 0.4f];
        var device = MemoryRenderDevice.MonoFloat48k();
        device.Init(ArraySampleProvider.Mono48k(samples));
        device.Start();

        device.Render(2);   // pull part of the data
        device.Render(10);  // ask for more than is left

        Assert.Equal(samples, device.Captured);
    }

    [Fact]
    public void MemoryRenderDevice_render_before_init_throws()
    {
        var device = MemoryRenderDevice.MonoFloat48k();
        device.Start();

        Assert.Throws<InvalidOperationException>(() => device.Render(4));
    }
}
