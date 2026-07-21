using AdaVoice.Audio.Abstractions;

namespace AdaVoice.Audio.Tests.Engine.Fakes;

/// <summary>A live monitor a test can inspect: records every Start/Stop call instead of touching
/// real audio, so Host tests can assert what the engine's PhraseChanged signal drove without any
/// WASAPI dependency.</summary>
public sealed class FakeLiveMonitor : ILiveMonitor
{
    public List<(float[] Samples, double Volume)> StartCalls { get; } = [];
    public int StopCount { get; private set; }

    public void Start(float[] samples, double volume) => StartCalls.Add((samples, volume));
    public void Stop() => StopCount++;
    public void Dispose() { }
}
