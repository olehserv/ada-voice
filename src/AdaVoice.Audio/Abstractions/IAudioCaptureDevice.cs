using NAudio.Wave;

namespace AdaVoice.Audio.Abstractions;

/// <summary>
/// Seam over a capture endpoint — the hardware mic in production, a WAV file in tests.
/// The audio core depends only on this interface, never on <c>WasapiCapture</c>, so the
/// engine can be driven entirely by fake devices in unit tests (design 08 §1).
/// </summary>
public interface IAudioCaptureDevice : IDisposable
{
    /// <summary>Native format the device delivers buffers in (converted to the engine
    /// format — 48 kHz float mono — downstream).</summary>
    WaveFormat Format { get; }

    DeviceState State { get; }

    /// <summary>Raised when a buffer of captured audio is available, on a device thread.</summary>
    event EventHandler<CaptureBufferEventArgs>? DataAvailable;

    event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    void Start();
    void Stop();
}

/// <summary>A chunk of captured audio in the device's native <see cref="WaveFormat"/>.</summary>
public sealed class CaptureBufferEventArgs(byte[] buffer, int bytesRecorded) : EventArgs
{
    public byte[] Buffer { get; } = buffer;
    public int BytesRecorded { get; } = bytesRecorded;
}
