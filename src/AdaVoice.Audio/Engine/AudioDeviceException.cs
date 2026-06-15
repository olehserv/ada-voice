namespace AdaVoice.Audio.Engine;

/// <summary>
/// Thrown by an <see cref="IAudioDeviceFactory"/> when it cannot create a device.
/// <see cref="IsTransient"/> tells the engine whether to keep retrying (device busy or
/// absent) or to give up and stop (a non-recoverable configuration error).
/// </summary>
public sealed class AudioDeviceException(string message, bool isTransient) : Exception(message)
{
    public bool IsTransient { get; } = isTransient;
}
