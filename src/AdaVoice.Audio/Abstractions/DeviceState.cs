namespace AdaVoice.Audio.Abstractions;

/// <summary>Lifecycle of a single hardware stream behind a device seam.</summary>
public enum DeviceState
{
    Stopped,
    Running,
    Faulted,
}

/// <summary>Raised when a device stream changes state (e.g. a fault on device loss).</summary>
public sealed class DeviceStateChangedEventArgs(DeviceState state, Exception? error = null) : EventArgs
{
    public DeviceState State { get; } = state;

    /// <summary>Set when <see cref="State"/> is <see cref="DeviceState.Faulted"/>.</summary>
    public Exception? Error { get; } = error;
}
