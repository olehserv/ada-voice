using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Core.Domain;
using AdaVoice.Host;

namespace AdaVoice.App.Tests;

/// <summary>A test double for the host seams: records the calls the view-models make, can raise state
/// changes, and grows its phrase list when a take is saved. Mirrors the real <c>EngineHost</c>, which
/// implements every seam on one object.</summary>
internal sealed class FakePlaybackHost : IPlaybackHost, IRecorderHost, ILibraryHost
{
    private List<PhraseEntry> _phrases = [];

    public EngineState State { get; set; } = EngineState.Stopped;

    public IReadOnlyList<PhraseEntry> Phrases
    {
        get => _phrases;
        set => _phrases = value.ToList();
    }

    // ---- ILibraryHost knobs the tests configure or inspect ----
    public IReadOnlyList<Category> Categories { get; set; } = [];
    public IReadOnlyList<string> BrokenPhraseIds { get; set; } = [];
    public List<PhraseEntry> Deleted { get; } = [];

    public event EventHandler<EngineState>? StateChanged;
    public event EventHandler<string?>? PlayingPhraseChanged;

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

    public void RaisePlayingPhraseChanged(string? id) => PlayingPhraseChanged?.Invoke(this, id);

    // ---- ILibraryHost ----
    public PhraseEntry? SetPhraseTitle(string phraseId, string title) =>
        Edit(phraseId, p => p with { Title = title.Trim() });

    public PhraseEntry? SetPhraseCategory(string phraseId, string categoryId) =>
        Edit(phraseId, p => p with { CategoryId = categoryId });

    public PhraseEntry? SetPhraseTags(string phraseId, IEnumerable<string> tags) =>
        Edit(phraseId, p => p with { Tags = tags.Select(t => t.Trim()).Where(t => t.Length > 0).ToArray() });

    public PhraseEntry? DeleteEntry(PhraseEntry entry)
    {
        Calls.Add("DeleteEntry");
        var existing = _phrases.FirstOrDefault(p => p.Id == entry.Id);
        if (existing is null)
            return null;

        _phrases.Remove(existing);
        Deleted.Add(existing);
        return existing;
    }

    private PhraseEntry? Edit(string phraseId, Func<PhraseEntry, PhraseEntry> edit)
    {
        var index = _phrases.FindIndex(p => p.Id == phraseId);
        if (index < 0)
            return null;

        var updated = edit(_phrases[index]);
        _phrases[index] = updated;
        return updated;
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
