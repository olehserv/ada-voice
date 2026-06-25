using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Core.Domain;
using AdaVoice.Host;

namespace AdaVoice.App.Tests;

/// <summary>A test double for both host seams: records the calls the view-models make, can raise state
/// changes, and grows its phrase list when a take is saved.</summary>
internal sealed class FakePlaybackHost : IPlaybackHost, IRecorderHost
{
    private List<PhraseEntry> _phrases = [];

    public EngineState State { get; set; } = EngineState.Stopped;

    public IReadOnlyList<PhraseEntry> Phrases
    {
        get => _phrases;
        set => _phrases = value.ToList();
    }

    public event EventHandler<EngineState>? StateChanged;

    public List<string> Calls { get; } = [];
    public PhraseEntry? PlayedEntry { get; private set; }

    // Recording knobs/results the tests configure or inspect.
    public bool CanRecord { get; set; } = true;
    public RecordingResult? NextStopResult { get; set; }
    public string? SavedTitle { get; private set; }
    public float[]? PreviewedSamples { get; private set; }

    // ---- IPlaybackHost ----
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

    // ---- IRecorderHost ----
    public bool TryStartRecording()
    {
        Calls.Add("TryStartRecording");
        return CanRecord;
    }

    public RecordingResult? StopRecording()
    {
        Calls.Add("StopRecording");
        return NextStopResult;
    }

    public PhraseEntry SaveTake(RecordingResult result, string title)
    {
        Calls.Add("SaveTake");
        SavedTitle = title;
        var entry = new PhraseEntry { Id = "p-saved", Title = title };
        _phrases.Add(entry);
        return entry;
    }

    public string? Preview(float[] samples, double gainDb)
    {
        Calls.Add("Preview");
        PreviewedSamples = samples;
        return null;
    }
}
