using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Engine.Fakes;

/// <summary>A capture device a test can drive: push samples, and fault on command.</summary>
public sealed class ControllableCaptureDevice : IAudioCaptureDevice
{
    public WaveFormat Format => TestAudio.EngineFormat;
    public DeviceState State { get; private set; } = DeviceState.Stopped;

    /// <summary>How many times Dispose ran — rebuild tests assert the old device was released
    /// exactly once (the device-lifecycle bug class the fakes were previously blind to).</summary>
    public int DisposeCount { get; private set; }

    public event EventHandler<CaptureBufferEventArgs>? DataAvailable;
    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public void Start() => State = DeviceState.Running;
    public void Stop() => State = DeviceState.Stopped;

    /// <summary>Push one block of mono float samples, as if the mic produced them.</summary>
    public void Push(float[] samples)
    {
        var bytes = TestAudio.ToBytes(samples);
        DataAvailable?.Invoke(this, new CaptureBufferEventArgs(bytes, bytes.Length));
    }

    /// <summary>Simulate a driver/device failure.</summary>
    public void Fault(Exception error)
    {
        State = DeviceState.Faulted;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(DeviceState.Faulted, error));
    }

    public void Dispose() => DisposeCount++;
}
