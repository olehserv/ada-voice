using AdaVoice.Audio.Engine;

namespace AdaVoice.Host;

/// <summary>
/// Payload of <see cref="IPlaybackHost.StateChanged"/>: the new state plus the engine's failure
/// reason when the transition was caused by an error (e.g. "cable not at 48 kHz" on a refused
/// Start). Carrying the reason across the seam is what lets the UI explain a dead Start button
/// instead of showing a bare "Stopped".
/// </summary>
public sealed class EngineStateChangedEventArgs(EngineState state, string? error = null) : EventArgs
{
    public EngineState State { get; } = state;

    /// <summary>Why the engine landed in this state, or null for a normal transition.</summary>
    public string? Error { get; } = error;
}
