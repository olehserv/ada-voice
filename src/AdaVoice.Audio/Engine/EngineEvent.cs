using AdaVoice.Audio.Passthrough;

namespace AdaVoice.Audio.Engine;

/// <summary>Why the engine landed in a state via an error transition — the App layer's localization
/// key, since Audio carries no display text.</summary>
public enum EngineErrorReason
{
    /// <summary>Wraps a caught exception's message (e.g. opening the audio graph failed). Framework
    /// text in <see cref="EngineError.Detail"/> stays English by design — a system detail, not our
    /// own text.</summary>
    DeviceFailure,
    DeviceChanged,
    CableStalled,

    /// <summary>A capture device handed us more than 2 channels (e.g. a multi-capsule USB mic) —
    /// <see cref="EngineError.Channels"/> carries the count for the localized message.</summary>
    TooManyMicChannels,

    /// <summary>The cable (or its monitor) is not delivering 48 kHz — same condition the setup
    /// wizard's environment check reports, just hit live instead of during setup.</summary>
    CableSampleRateMismatch,
}

/// <summary>Why an error-driven state transition happened. <see cref="Detail"/> is only meaningful
/// (and only ever English) for <see cref="EngineErrorReason.DeviceFailure"/>; for the other reasons
/// it is diagnostic-only (never displayed) — e.g. which role/change caused it. <see cref="Channels"/>
/// is only meaningful for <see cref="EngineErrorReason.TooManyMicChannels"/>.</summary>
public sealed record EngineError(EngineErrorReason Reason, string? Detail = null, int? Channels = null);

/// <summary>Immutable notifications the engine raises out (AudioEngine design spec §2.3). The engine
/// itself does no logging. The host marshals <see cref="StateChanged"/>/<see cref="DriftLogged"/>/
/// <see cref="RebuildResult"/> to the UI thread and logs them; <see cref="PhraseChanged"/> can arrive
/// on the render thread instead (see its own doc), so the host re-raises it without logging rather
/// than doing file I/O on that thread.</summary>
public abstract record EngineEvent
{
    public sealed record StateChanged(EngineState State, EngineError? Error = null) : EngineEvent;
    public sealed record DriftLogged(DriftKind Kind) : EngineEvent;
    public sealed record RebuildResult(DeviceRole Role, bool Success, int Attempt) : EngineEvent;

    /// <summary>The phrase now playing (its id), or null when playback stops. For the UI's playing glow.
    /// Unlike the other three cases, this can fire on the audio render thread (under the mixer lock) on
    /// a natural phrase end — a subscriber must marshal it itself.</summary>
    public sealed record PhraseChanged(string? PhraseId) : EngineEvent;
}
