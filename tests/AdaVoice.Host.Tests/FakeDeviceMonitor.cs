using AdaVoice.Audio.Abstractions;

namespace AdaVoice.Host.Tests;

/// <summary>A device monitor a test can drive by hand; no COM.</summary>
public sealed class FakeDeviceMonitor : IDeviceMonitor
{
    public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

    public bool Started { get; private set; }

    public void Start() => Started = true;
    public void Stop() => Started = false;
    public void Dispose() { }

    public void Raise(DeviceChangeKind kind, string deviceId) =>
        DeviceChanged?.Invoke(this, new DeviceChangeEventArgs(kind, deviceId));
}
