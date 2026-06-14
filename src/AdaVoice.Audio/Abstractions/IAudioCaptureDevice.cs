using NAudio.Wave;

namespace AdaVoice.Audio.Abstractions;

/// <summary>
/// One source of captured audio. In the real app this is the microphone.
/// In tests it can be a WAV file instead.
/// The audio core uses only this interface. It never uses NAudio's <c>WasapiCapture</c>
/// directly. This lets tests run the engine with fake devices (design 08 §1).
/// </summary>
public interface IAudioCaptureDevice : IDisposable
{
    /// <summary>The audio format that this device sends. The engine later converts it
    /// to its own format (48 kHz, float, mono).</summary>
    WaveFormat Format { get; }

    DeviceState State { get; }

    /// <summary>Sent when a new block of captured audio is ready. It runs on a device thread.</summary>
    event EventHandler<CaptureBufferEventArgs>? DataAvailable;

    event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    void Start();
    void Stop();
}

/// <summary>One block of captured audio, in the device's own <see cref="WaveFormat"/>.</summary>
public sealed class CaptureBufferEventArgs(byte[] buffer, int bytesRecorded) : EventArgs
{
    public byte[] Buffer { get; } = buffer;
    public int BytesRecorded { get; } = bytesRecorded;
}
