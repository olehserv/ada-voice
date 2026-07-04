using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// The Board: shows the phrases, plays them to the call, records new ones, and edits/deletes them. Talks
/// only to the host seams (<see cref="IPlaybackHost"/> / <see cref="IRecorderHost"/> /
/// <see cref="ILibraryHost"/> / <see cref="ISetupHost"/> / <see cref="ISettingsHost"/>), so it is
/// unit-testable with a fake. Editing and deleting use injected dialog callbacks so the view-model
/// needs no XAML to test.
/// </summary>
public partial class BoardViewModel : ObservableObject
{
    private readonly IPlaybackHost _playback;
    private readonly IRecorderHost _recorder;
    private readonly ILibraryHost _library;
    private readonly ISetupHost _setup;
    private readonly ISettingsHost _settingsHost;
    private readonly Func<string?> _getActiveHotkey;
    private readonly Action<SetupWizardViewModel> _showSetupWizard;
    private readonly Action<SettingsWindowViewModel> _showSettings;
    private readonly Func<string?> _pickExportPath;
    private readonly Func<(string Path, ImportMode Mode)?> _pickImportFile;
    private readonly Action _confirmAndRestart;
    private readonly Action<string> _showError;
    private readonly Action<string> _showSettingsInfo;
    private readonly Func<PhraseItemViewModel, bool> _confirmDelete;
    private readonly Func<PhraseEditViewModel, bool> _showEditDialog;
    private readonly Action<CategoriesViewModel> _showManageCategories;
    private readonly Action<Action> _onUiThread;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecordButton))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingTake))]
    [NotifyPropertyChangedFor(nameof(ShowRecordButton))]
    [NotifyPropertyChangedFor(nameof(PendingTakeDurationLabel))]
    private RecordingResult? _pendingTake;

    [ObservableProperty]
    private string _newTitle = "";

    [ObservableProperty]
    private string? _notice;

    /// <summary>Live title/tag search. Empty matches everything.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>The category to show, or the "All categories" sentinel for no category filter.</summary>
    [ObservableProperty]
    private Category _selectedCategoryFilter;

    public BoardViewModel(IPlaybackHost playback, IRecorderHost recorder, ILibraryHost library, ISetupHost setup,
        ISettingsHost settingsHost, StatusViewModel status, SettingsViewModel settings,
        Func<string?>? getActiveHotkey = null,
        Action<Action>? onUiThread = null,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<SetupWizardViewModel>? showSetupWizard = null,
        Action<SettingsWindowViewModel>? showSettings = null,
        Func<string?>? pickExportPath = null,
        Func<(string Path, ImportMode Mode)?>? pickImportFile = null,
        Action? confirmAndRestart = null,
        Action<string>? showError = null,
        Action<string>? showSettingsInfo = null)
    {
        _playback = playback;
        _recorder = recorder;
        _library = library;
        _setup = setup;
        _settingsHost = settingsHost;
        _getActiveHotkey = getActiveHotkey ?? (() => null); // default: no hotkey (unit tests)
        _onUiThread = onUiThread ?? (action => action()); // default: inline (unit tests)
        _confirmDelete = confirmDelete ?? (_ => true);     // default: confirm (unit tests)
        _showEditDialog = showEditDialog ?? (_ => false);  // default: cancel (unit tests opt in)
        _showManageCategories = showManageCategories ?? (_ => { }); // default: no-op (unit tests)
        _showSetupWizard = showSetupWizard ?? (_ => { });  // default: no-op (unit tests)
        _showSettings = showSettings ?? (_ => { });        // default: no-op (unit tests)
        _pickExportPath = pickExportPath ?? (() => null);  // default: cancelled (unit tests)
        _pickImportFile = pickImportFile ?? (() => null);  // default: cancelled (unit tests)
        _confirmAndRestart = confirmAndRestart ?? (() => { }); // default: no-op (unit tests)
        _showError = showError ?? (_ => { });              // default: no-op (unit tests)
        _showSettingsInfo = showSettingsInfo ?? (_ => { }); // default: no-op (unit tests)
        Status = status;
        Settings = settings;
        var broken = library.BrokenPhraseIds.ToHashSet();
        Phrases = new ObservableCollection<PhraseItemViewModel>(
            _library.Phrases.Select(e => new PhraseItemViewModel(e) { IsBroken = broken.Contains(e.Id) }));

        // A problem loading the library must be visible, or an empty board looks like an empty library.
        _notice = library.LibraryWarning;

        ApplyColors(); // tint each tile with its category colour and resolve its tag chips

        // "All categories" + the real categories drive the filter dropdown; default to All.
        CategoryFilterOptions = [AllCategories, .. library.Categories];
        _selectedCategoryFilter = AllCategories;

        // A filtered view over the same collection — the grid binds to this, not to Phrases directly.
        PhrasesView = CollectionViewSource.GetDefaultView(Phrases);
        PhrasesView.Filter = o => o is PhraseItemViewModel p && Matches(p.Entry, SearchText, EffectiveCategoryId);

        Phrases.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPhrases));
            OnPropertyChanged(nameof(NoMatches));
            OnPropertyChanged(nameof(HasMatches));
        };
        _playback.PlayingPhraseChanged += OnPlayingPhraseChanged;
    }

    /// <summary>Sentinel "show every category" option for the filter dropdown (blank id = no filter).</summary>
    public static readonly Category AllCategories = new() { Id = "", Name = "All categories" };

    /// <summary>Raised after a take is saved, with its title — the view shows a "Saved" toast.</summary>
    public event EventHandler<string>? Saved;

    /// <summary>Raised after a phrase is deleted, with its title — the view shows a "Deleted" toast.</summary>
    public event EventHandler<string>? Deleted;

    public StatusViewModel Status { get; }

    /// <summary>The inline settings (the duck-level slider) bound from the status bar.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>The phrase buttons. An ObservableCollection so the UI updates when one is added; each
    /// item carries its own UI state (e.g. the playing glow). The library list stays the source of
    /// truth; this mirrors it for the UI.</summary>
    public ObservableCollection<PhraseItemViewModel> Phrases { get; }

    /// <summary>True when a just-recorded take is waiting to be named/saved.</summary>
    public bool HasPendingTake => PendingTake is not null;

    /// <summary>The pending take's length in seconds (e.g. "5.7 s"), matching the phrase tiles.</summary>
    public string PendingTakeDurationLabel => PendingTake is { } take ? $"{take.DurationMs / 1000.0:0.0} s" : "";

    /// <summary>The idle "Record" button shows only when not recording and no take is pending.</summary>
    public bool ShowRecordButton => !IsRecording && !HasPendingTake;

    /// <summary>The filtered, ordered view of <see cref="Phrases"/> the grid binds to.</summary>
    public ICollectionView PhrasesView { get; }

    /// <summary>"All categories" followed by the real categories — the filter dropdown's items. Rebuilt
    /// after the category manager runs, since categories may have been added/renamed/deleted.</summary>
    public IReadOnlyList<Category> CategoryFilterOptions { get; private set; }

    /// <summary>True when the board has no phrases at all — the view shows a first-run welcome card.</summary>
    public bool IsEmpty => Phrases.Count == 0;

    /// <summary>Inverse of <see cref="IsEmpty"/>.</summary>
    public bool HasPhrases => !IsEmpty;

    /// <summary>Phrases exist but the current search/filter hides them all — a distinct "no matches"
    /// state, separate from the first-run welcome (<see cref="IsEmpty"/>).</summary>
    public bool NoMatches => HasPhrases && PhrasesView.IsEmpty;

    /// <summary>At least one phrase is visible under the current filter — the grid binds to this.</summary>
    public bool HasMatches => HasPhrases && !PhrasesView.IsEmpty;

    private string? EffectiveCategoryId =>
        string.IsNullOrEmpty(SelectedCategoryFilter?.Id) ? null : SelectedCategoryFilter.Id;

    /// <summary>A phrase matches when its category passes the filter and the search text appears in its
    /// title or any tag. Pure, so it is unit-testable without WPF.</summary>
    private static bool Matches(PhraseEntry entry, string? search, string? categoryId)
    {
        if (categoryId is not null && entry.CategoryId != categoryId)
            return false;
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var term = search.Trim();
        return entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Open the category manager; when it closes, rebuild the filter dropdown (categories may
    /// have changed) and reset the filter to "All".</summary>
    [RelayCommand]
    private void ManageCategories()
    {
        _showManageCategories(new CategoriesViewModel(_library));

        CategoryFilterOptions = [AllCategories, .. _library.Categories];
        OnPropertyChanged(nameof(CategoryFilterOptions));
        ApplyColors(); // categories may have been recoloured or deleted
        SelectedCategoryFilter = AllCategories; // also refreshes the filter
    }

    /// <summary>Open the setup wizard on demand (re-run entry point). Always builds a fresh wizard
    /// so a re-run never shows stale check results from a previous run.</summary>
    [RelayCommand]
    private void RunSetup() => _showSetupWizard(new SetupWizardViewModel(_setup, _getActiveHotkey()));

    /// <summary>Open the Settings window on demand. Always builds a fresh view-model so a re-open
    /// never shows a stale hotkey status or backup date from a previous open.</summary>
    [RelayCommand]
    private void RunSettings() => _showSettings(new SettingsWindowViewModel(
        _settingsHost, _setup, _getActiveHotkey(), _pickExportPath, _pickImportFile,
        _confirmAndRestart, _showError, _showSettingsInfo));

    partial void OnSearchTextChanged(string value) => RefreshFilter();
    partial void OnSelectedCategoryFilterChanged(Category value) => RefreshFilter();

    private void RefreshFilter()
    {
        PhrasesView.Refresh();
        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(HasMatches));
    }

    /// <summary>Tint every phrase tile with its category's colour and resolve its tags into coloured
    /// chips. Called when the board is built, after an edit (category/tags may have changed), after the
    /// category manager closes (colours may have changed) and after a take is saved. Cheap for a board
    /// of a few dozen phrases.</summary>
    private void ApplyColors()
    {
        var colorById = _library.Categories.ToDictionary(c => c.Id, c => c.Color);
        // Case-insensitive: a phrase can store a tag as "Opening" while the registry has "opening" (the
        // registry keeps whichever casing was used first) — an ordinal lookup would miss the match and
        // leave the chip uncoloured.
        var tagColorByName = _library.Tags.ToDictionary(t => t.Name, t => t.Color, StringComparer.OrdinalIgnoreCase);

        foreach (var item in Phrases)
        {
            item.CategoryColor = colorById.TryGetValue(item.CategoryId, out var hex) ? hex : "";
            item.TagChips = item.Tags
                .Select(name => new TagChipViewModel(name, tagColorByName.TryGetValue(name, out var color) ? color : ""))
                .ToList();
        }
    }

    // ---- Playback -------------------------------------------------------------------------------

    /// <summary>Left-click a phrase: play it to the call. The action is gated (not the button), so the
    /// button still opens its right-click menu when the engine is stopped or the audio is missing.</summary>
    [RelayCommand]
    private void Play(PhraseItemViewModel? item)
    {
        if (item is null)
            return;

        if (item.IsBroken)
            Notice = "This phrase's audio file is missing — it can't be played.";
        else if (_playback.State != EngineState.Live)
            Notice = "Start the engine (and be ON AIR) to play to the call.";
        else
        {
            Notice = null;
            _playback.PlayEntry(item.Entry);
        }
    }

    /// <summary>Right-click "Test on headphones": preview a phrase on the monitor output, engine or not.
    /// Preview blocks until playback ends, so it runs off the UI thread; a failure becomes a notice.</summary>
    [RelayCommand]
    private async Task TestOnHeadphones(PhraseItemViewModel? item)
    {
        if (item is null)
            return;

        Notice = null; // clear any stale notice from a prior action before we preview
        try
        {
            var error = await Task.Run(() => _playback.PreviewEntry(item.Entry));
            if (error is not null)
                _onUiThread(() => Notice = error);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Corrupt WAV, no output device, COM failure — surface it instead of crashing the app.
            _onUiThread(() => Notice = "Could not play the preview — check the playback device and try again.");
        }
    }

    [RelayCommand]
    private void Stop() => _playback.StopPhrase();

    // ---- Edit / delete --------------------------------------------------------------------------

    /// <summary>Open the edit dialog for a phrase; on commit, write the changes and refresh the item in
    /// place (the wrapped entry is immutable, so the VM must be told to re-read).</summary>
    [RelayCommand]
    private void Edit(PhraseItemViewModel? item)
    {
        if (item is null)
            return;

        var edit = new PhraseEditViewModel(_library, item.Entry);
        if (!_showEditDialog(edit))
            return; // cancelled

        if (edit.Save() is { } updated)
        {
            item.Update(updated);
            ApplyColors(); // the category or tags may have changed → re-tint / re-chip
            // Update swaps the entry in place (no CollectionChanged), so re-run the filter — a rename or
            // category change can move the item in or out of the current search/category view.
            RefreshFilter();
        }
    }

    /// <summary>Delete a phrase after confirmation: orphan its WAV, drop it from the board, and toast.</summary>
    [RelayCommand]
    private void Delete(PhraseItemViewModel? item)
    {
        if (item is null || !_confirmDelete(item))
            return;

        var title = item.Title;
        _library.DeleteEntry(item.Entry);
        Phrases.Remove(item);
        Deleted?.Invoke(this, title);
    }

    /// <summary>Reflect the engine's currently-playing phrase as the per-item glow. Fires off the UI
    /// thread, so marshal before touching the bound items.</summary>
    private void OnPlayingPhraseChanged(object? sender, string? playingId) =>
        _onUiThread(() =>
        {
            foreach (var item in Phrases)
                item.IsPlaying = playingId is not null && item.Entry.Id == playingId;
        });

    [RelayCommand]
    private void StartEngine() => _playback.Start();

    [RelayCommand]
    private void StopEngine() => _playback.Stop();

    [RelayCommand]
    private void ToggleOffAir()
    {
        if (_playback.State == EngineState.OffAir)
            _playback.ExitOffAir();
        else
            _playback.EnterOffAir();
    }

    // ---- Recording ------------------------------------------------------------------------------

    [RelayCommand]
    private void StartRecording()
    {
        Notice = null;
        if (_recorder.TryStartRecording())
            IsRecording = true;
        else
            Notice = "Press Start to go Live before recording.";
    }

    [RelayCommand]
    private void StopRecording()
    {
        IsRecording = false;
        var take = _recorder.StopRecording();
        if (take is { HasSignal: true })
        {
            PendingTake = take;
            NewTitle = $"Take {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            Notice = null;
        }
        else
        {
            PendingTake = null;
            Notice = "No signal — nothing recorded.";
        }
    }

    /// <summary>Preview the pending take on the monitor output. Preview blocks until playback
    /// ends, so it runs off the UI thread (same rule as <see cref="TestOnHeadphones"/>) —
    /// a synchronous call would freeze the window, STOP, and the global hotkey for the
    /// take's full length.</summary>
    [RelayCommand]
    private async Task PreviewTake()
    {
        if (PendingTake is not { } take)
            return;

        Notice = "Previewing…";
        try
        {
            var error = await Task.Run(() => _recorder.Preview(take.Samples, take.GainDb));
            if (error is not null)
                _onUiThread(() => Notice = error);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _onUiThread(() => Notice = "Could not play the preview — check the playback device and try again.");
        }
    }

    [RelayCommand]
    private void SaveTake()
    {
        if (PendingTake is not { } take)
            return;

        var entry = _recorder.SaveTake(take, NewTitle);
        PendingTake = null;
        Notice = null; // the "Saved" feedback is now a toast (see Saved)
        Phrases.Add(new PhraseItemViewModel(entry)); // appears on the board immediately
        ApplyColors(); // tint the new tile (falls back to its default category colour)
        Saved?.Invoke(this, entry.Title);
    }

    [RelayCommand]
    private void DiscardTake()
    {
        PendingTake = null;
        Notice = "Take discarded.";
    }
}
