namespace AdaVoice.Audio.Abstractions;

/// <summary>
/// Reports changes to audio devices in the system. In the real app this wraps NAudio's
/// <c>IMMNotificationClient</c>. In tests we can send these events by hand (design 08 §1).
/// When a device is added, removed, or set as default, the engine can rebuild only the
/// stream that changed. It does not need to rebuild the whole graph.
/// </summary>
public interface IDeviceMonitor : IDisposable
{
    event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

    void Start();
    void Stop();
}

public enum DeviceChangeKind { Added, Removed, DefaultChanged }

public sealed class DeviceChangeEventArgs(DeviceChangeKind kind, string deviceId) : EventArgs
{
    public DeviceChangeKind Kind { get; } = kind;
    public string DeviceId { get; } = deviceId;
}
