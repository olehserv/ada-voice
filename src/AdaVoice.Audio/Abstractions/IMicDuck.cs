namespace AdaVoice.Audio.Abstractions;

/// <summary>
/// Lets the phrase player lower (duck) and restore the live mic gain. The mic passthrough
/// provides this. Keeping it as a small interface means the player can be tested without a
/// real mic.
/// </summary>
public interface IMicDuck
{
    /// <summary>Move the mic gain to <paramref name="targetGain"/> over <paramref name="rampMs"/>.</summary>
    void Duck(float targetGain, int rampMs);
}
