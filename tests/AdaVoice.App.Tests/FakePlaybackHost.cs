using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Audio.Setup;
using AdaVoice.Core.Domain;
using AdaVoice.Host;

namespace AdaVoice.App.Tests;

/// <summary>A test double for the host seams: records the calls the view-models make, can raise state
/// changes, and grows its phrase list when a take is saved. Mirrors the real <c>EngineHost</c>, which
/// implements every seam on one object.</summary>
internal sealed class FakePlaybackHost : IPlaybackHost, IRecorderHost, ILibraryHost, ISetupHost
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
    public IReadOnlyList<TagInfo> Tags { get; set; } = [];
    public IReadOnlyList<string> BrokenPhraseIds { get; set; } = [];
    public List<PhraseEntry> Deleted { get; } = [];

    public event EventHandler<EngineStateChangedEventArgs>? StateChanged;
    public event EventHandler<string?>? PlayingPhraseChanged;

    public List<string> Calls { get; } = [];
    public PhraseEntry? PlayedEntry { get; private set; }

    // Recording knobs/results the tests configure or inspect.
    public bool CanRecord { get; set; } = true;
    public RecordingResult? NextStopResult { get; set; }
    public string? SavedTitle { get; private set; }
    public float[]? PreviewedSamples { get; private set; }

    // Preview-to-headphones knobs the tests inspect / configure.
    public PhraseEntry? PreviewedEntry { get; private set; }
    public string? PreviewEntryResult { get; set; }

    // ---- ISetupHost knobs the tests configure or inspect ----
    public IReadOnlyList<EnvironmentCheck> NextChecks { get; set; } = [];
    public CalibrationResult NextCalibrationResult { get; set; } = new(true, 0.05, null);
    public bool CalibrateThrows { get; set; }

    public IReadOnlyList<EnvironmentCheck> RunEnvironmentChecks()
    {
        Calls.Add("RunEnvironmentChecks");
        return NextChecks;
    }

    public CalibrationResult Calibrate(int seconds = 5)
    {
        Calls.Add("Calibrate");
        if (CalibrateThrows)
            throw new InvalidOperationException("mic busy (simulated)");
        return NextCalibrationResult;
    }

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

    public string? PreviewEntry(PhraseEntry entry)
    {
        Calls.Add("PreviewEntry");
        PreviewedEntry = entry;
        return PreviewEntryResult;
    }

    public void RaiseStateChanged(EngineState state, string? error = null)
    {
        State = state;
        StateChanged?.Invoke(this, new EngineStateChangedEventArgs(state, error));
    }

    public void RaisePlayingPhraseChanged(string? id) => PlayingPhraseChanged?.Invoke(this, id);

    // ---- ILibraryHost ----
    public PhraseEntry? SetPhraseTitle(string phraseId, string title) =>
        Edit(phraseId, p => p with { Title = title.Trim() });

    public PhraseEntry? SetPhraseCategory(string phraseId, string categoryId) =>
        Edit(phraseId, p => p with { CategoryId = categoryId });

    public PhraseEntry? SetPhraseTags(string phraseId, IEnumerable<string> tags)
    {
        var normalized = tags.Select(t => t.Trim()).Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Mirror PhraseLibraryService: register new names into the tag registry (case-insensitive,
        // cycling the palette) so board tests that add a tag then read its chip colour see the real flow.
        var registry = Tags.ToList();
        foreach (var name in normalized)
            if (!registry.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
                registry.Add(new TagInfo { Name = name, Color = ColorPalette.Swatches[registry.Count % ColorPalette.Swatches.Count] });
        Tags = registry;

        return Edit(phraseId, p => p with { Tags = normalized });
    }

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

    public Category AddCategory(string name, string color)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("blank", nameof(name));

        var category = new Category { Id = "c-" + (Categories.Count + 1), Name = name.Trim(), Color = color };
        Categories = [.. Categories, category];
        return category;
    }

    public Category? UpdateCategory(string id, string name, string color)
    {
        var existing = Categories.FirstOrDefault(c => c.Id == id);
        if (existing is null)
            return null;

        var updated = existing with { Name = name.Trim(), Color = color };
        Categories = Categories.Select(c => c.Id == id ? updated : c).ToList();
        return updated;
    }

    public bool DeleteCategory(string id)
    {
        if (id == Category.DefaultId || Categories.All(c => c.Id != id))
            return false;

        Categories = Categories.Where(c => c.Id != id).ToList();
        return true;
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
        // Mirror EngineHost: a new take lands in the default category.
        var entry = new PhraseEntry { Id = "p-saved", Title = title, CategoryId = Category.DefaultId };
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
