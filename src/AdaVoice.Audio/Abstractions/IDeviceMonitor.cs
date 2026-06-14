namespace AdaVoice.Audio.Abstractions;

/// <summary>
/// Seam over system device-change notifications. Production wraps
/// <c>IMMNotificationClient</c>; tests fire events synthetically (design 08 §1).
/// Lets the engine rebuild only the affected stream on add / remove / default-change,
/// instead of tearing down the whole graph.
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
