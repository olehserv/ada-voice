using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Playback;

namespace AdaVoice.Audio.Engine;

/// <summary>Immutable messages fed into the engine's command queue (AudioEngine design spec §2.3).</summary>
public abstract record EngineCommand
{
    public sealed record Start : EngineCommand;
    public sealed record Stop : EngineCommand;
    public sealed record Play(Phrase Phrase) : EngineCommand;
    public sealed record StopPhrase : EngineCommand;
    public sealed record EnterOffAir : EngineCommand;
    public sealed record ExitOffAir : EngineCommand;

    /// <summary>A device was added, removed, or set as default (from the device monitor).</summary>
    public sealed record DeviceChanged(DeviceRole Role, DeviceChangeKind Kind) : EngineCommand;

    /// <summary>
    /// A live stream raised an error. <see cref="Error"/> may be null when the fault was
    /// detected without an exception object (e.g. a driver reports Faulted with no detail).
    /// </summary>
    public sealed record StreamFaulted(DeviceRole Role, Exception? Error) : EngineCommand;

    /// <summary>Periodic watchdog/rebuild tick.</summary>
    public sealed record WatchdogTick : EngineCommand;
}
