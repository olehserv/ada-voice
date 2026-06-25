using AdaVoice.Audio.Engine;
using AdaVoice.Core.Domain;
using AdaVoice.Host;

namespace AdaVoice.App.Tests;

/// <summary>A test double that records the calls the view-models make and can raise state changes.</summary>
internal sealed class FakePlaybackHost : IPlaybackHost
{
    public EngineState State { get; set; } = EngineState.Stopped;
    public IReadOnlyList<PhraseEntry> Phrases { get; set; } = [];

    public event EventHandler<EngineState>? StateChanged;

    public List<string> Calls { get; } = [];
    public PhraseEntry? PlayedEntry { get; private set; }

    public void Start() => Calls.Add("Start");
    public void Stop() => Calls.Add("Stop");
    public void StopPhrase() => Calls.Add("StopPhrase");
    public void EnterOffAir() => Calls.Add("EnterOffAir");
    public void ExitOffAir() => Calls.Add("ExitOffAir");

    public void PlayEntry(PhraseEntry entry)
    {
        Calls.Add("PlayEntry");
        PlayedEntry = entry;
    }

    public void RaiseStateChanged(EngineState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }
}
