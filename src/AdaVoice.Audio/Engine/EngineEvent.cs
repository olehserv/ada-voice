using AdaVoice.Audio.Passthrough;

namespace AdaVoice.Audio.Engine;

/// <summary>Immutable notifications the engine raises out (AudioEngine design spec §2.3). The engine
/// itself does no logging. The host marshals <see cref="StateChanged"/>/<see cref="DriftLogged"/>/
/// <see cref="RebuildResult"/> to the UI thread and logs them; <see cref="PhraseChanged"/> can arrive
/// on the render thread instead (see its own doc), so the host re-raises it without logging rather
/// than doing file I/O on that thread.</summary>
public abstract record EngineEvent
{
    public sealed record StateChanged(EngineState State, string? Error = null) : EngineEvent;
    public sealed record DriftLogged(DriftKind Kind) : EngineEvent;
    public sealed record RebuildResult(DeviceRole Role, bool Success, int Attempt) : EngineEvent;

    /// <summary>The phrase now playing (its id), or null when playback stops. For the UI's playing glow.
    /// Unlike the other three cases, this can fire on the audio render thread (under the mixer lock) on
    /// a natural phrase end — a subscriber must marshal it itself.</summary>
    public sealed record PhraseChanged(string? PhraseId) : EngineEvent;
}
