namespace AdaVoice.Audio.Abstractions;

/// <summary>The state of one audio stream from a device.</summary>
public enum DeviceState
{
    Stopped,
    Running,
    Faulted,
}

/// <summary>Sent when a device stream changes its state. For example, when a device is lost.</summary>
public sealed class DeviceStateChangedEventArgs(DeviceState state, Exception? error = null) : EventArgs
{
    public DeviceState State { get; } = state;

    /// <summary>Holds the error when <see cref="State"/> is <see cref="DeviceState.Faulted"/>. It is null in other cases.</summary>
    public Exception? Error { get; } = error;
}
