using AdaVoice.Audio.Abstractions;

namespace AdaVoice.Audio.Passthrough;

/// <summary>
/// A stable duck target for the <see cref="Playback.PhrasePlayer"/> across mic rebuilds. The player
/// captures its <see cref="IMicDuck"/> once, at construction — but the engine replaces the
/// <see cref="MicPassthrough"/> after a mic fault. Without this relay, every later duck command
/// would land on the disposed old passthrough and the live mic would play at full volume under
/// every phrase for the rest of the session (review C1). The engine hands the relay to the player
/// and retargets it at the end of each mic rebuild.
/// </summary>
/// <remarks>
/// Thread-safety: <see cref="Duck"/> is called from the control thread and (on a phrase's natural
/// end) from the render thread; <see cref="Retarget"/> only from the control thread. The target
/// swap is an atomic reference write, and the underlying <see cref="MicPassthrough"/> duck ramp is
/// already safe to call from any thread — a duck racing a retarget lands on one of the two
/// passthroughs, and the retarget re-applies the last command anyway.
/// </remarks>
public sealed class MicDuckRelay(IMicDuck target) : IMicDuck
{
    private volatile IMicDuck _target = target;
    private float _lastGain = 1f; // full gain = not ducked; matches a fresh passthrough
    private int _lastRampMs;

    public void Duck(float targetGain, int rampMs)
    {
        _lastGain = targetGain;
        _lastRampMs = rampMs;
        _target.Duck(targetGain, rampMs);
    }

    /// <summary>Point at the rebuilt passthrough and re-apply the last duck command — a new
    /// passthrough starts at full gain, but a phrase may still be playing through the rebuild.</summary>
    public void Retarget(IMicDuck target)
    {
        _target = target;
        target.Duck(_lastGain, _lastRampMs);
    }
}
