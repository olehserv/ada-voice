namespace AdaVoice.Audio.Abstractions;

/// <summary>
/// Plays a phrase's already-decoded samples to a second output (the operator's own headphones)
/// alongside whatever the engine sends to the call cable, so the operator can confirm what the
/// other side hears. This is not part of the engine's audio graph — the mixer that feeds the
/// cable also carries the live mic, so tapping it would leak the operator's own voice back into
/// their headphones. A live monitor instead re-renders the same phrase samples through an
/// independent output.
/// </summary>
/// <remarks>
/// Fire-and-forget: <see cref="Start"/> returns immediately — the render happens off the
/// caller's thread, so it never blocks the engine's control thread. A second <see cref="Start"/>
/// call replaces whatever is currently playing, mirroring <c>PhrasePlayer</c>'s
/// replace-on-retrigger. Implementations must never let the resolved output be the call cable —
/// that would double-feed the call instead of just letting the operator listen in.
/// </remarks>
public interface ILiveMonitor : IDisposable
{
    /// <summary>Start (or replace) live monitoring of one phrase's samples at the given linear
    /// volume (0-1, where 1 is the same level the call hears).</summary>
    void Start(float[] samples, double volume);

    /// <summary>Stop monitoring, if anything is currently playing. Safe to call when idle.</summary>
    void Stop();
}
