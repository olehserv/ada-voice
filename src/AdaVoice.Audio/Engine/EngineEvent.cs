using AdaVoice.Audio.Passthrough;

namespace AdaVoice.Audio.Engine;

/// <summary>Immutable notifications the engine raises out (AudioEngine design spec §2.3). The host marshals
/// these to the UI thread and logs them; the engine itself does no logging.</summary>
public abstract record EngineEvent
{
    public sealed record StateChanged(EngineState State, string? Error = null) : EngineEvent;
    public sealed record DriftLogged(DriftKind Kind) : EngineEvent;
    public sealed record RebuildResult(DeviceRole Role, bool Success, int Attempt) : EngineEvent;

    /// <summary>The phrase now playing (its id), or null when playback stops. For the UI's playing glow.</summary>
    public sealed record PhraseChanged(string? PhraseId) : EngineEvent;
}
