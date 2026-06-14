using AdaVoice.Audio.Abstractions;

namespace AdaVoice.Audio.Tests.Fakes;

/// <summary>
/// Records every duck call so a test can check when the mic was lowered or restored.
/// </summary>
public sealed class DuckSpy : IMicDuck
{
    public List<(float Gain, int RampMs)> Calls { get; } = [];

    public float LastGain => Calls[^1].Gain;

    public void Duck(float targetGain, int rampMs) => Calls.Add((targetGain, rampMs));
}
