using AdaVoice.Audio.Abstractions;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Fakes;

/// <summary>
/// A fake capture device. It plays bytes that were given to it, as if they came from a
/// microphone. The test controls the pace by calling <see cref="Pump"/>. This lets the
/// engine be tested with no real hardware (design 08 §1).
/// </summary>
public sealed class FileCaptureDevice : IAudioCaptureDevice
{
    private readonly byte[] _data;
    private int _position;

    public FileCaptureDevice(byte[] data, WaveFormat format)
    {
        _data = data;
        Format = format;
    }

    /// <summary>Build a device from mono float samples in the engine format.</summary>
    public static FileCaptureDevice FromFloat(float[] samples) =>
        new(TestAudio.ToBytes(samples), TestAudio.EngineFormat);

    public WaveFormat Format { get; }
    public DeviceState State { get; private set; } = DeviceState.Stopped;

    public event EventHandler<CaptureBufferEventArgs>? DataAvailable;
    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public void Start() => SetState(DeviceState.Running);
    public void Stop() => SetState(DeviceState.Stopped);

    /// <summary>
    /// Send the next block of audio. The size is rounded down to a whole number of
    /// frames. Returns true if more data is left after this block.
    /// </summary>
    public bool Pump(int byteCount)
    {
        if (State != DeviceState.Running)
            throw new InvalidOperationException("Start the device before pumping data.");

        byteCount -= byteCount % Format.BlockAlign;
        var available = _data.Length - _position;
        var n = Math.Min(byteCount, available);
        if (n > 0)
        {
            var buffer = new byte[n];
            Array.Copy(_data, _position, buffer, 0, n);
            _position += n;
            DataAvailable?.Invoke(this, new CaptureBufferEventArgs(buffer, n));
        }

        return _position < _data.Length;
    }

    /// <summary>Send a block that holds about <paramref name="milliseconds"/> ms of audio.</summary>
    public bool PumpMilliseconds(int milliseconds) =>
        Pump(milliseconds * Format.AverageBytesPerSecond / 1000);

    public void Dispose() { }

    private void SetState(DeviceState state)
    {
        State = state;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(state));
    }
}
