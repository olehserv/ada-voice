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
    private readonly Func<RepairPhraseViewModel, bool> _showRepairDialog;
    private readonly Action<CategoriesViewModel> _showManageCategories;
    private readonly Action _showRecorder;
    private readonly Action<Action> _onUiThread;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecordButton))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingTake))]
    [NotifyPropertyChangedFor(nameof(ShowRecordButton))]
    [NotifyPropertyChangedFor(nameof(PendingTakeDurationLabel))]
    private RecordingResult? _pendingTake;

    /// <summary>True from the moment Stop is clicked until the pending take is ready (or the
    /// attempt fails) — bridges the async gap in <see cref="StopRecording"/> so the idle Record
    /// button does not flash back into view while the take is still being trimmed/loudness-matched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRecordButton))]
    private bool _isProcessing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveTakeCommand))]
    private string _newTitle = "";

    /// <summary>The last message raised for the operator (kept as state for tests and for the
    /// library-load warning the view reads on startup). The view shows messages as toasts via
    /// <see cref="Notified"/>, not by binding this.</summary>
    [ObservableProperty]
    private string? _notice;

    /// <summary>Raised when the operator should see a message; the view shows it as a
    /// severity-colored toast. Set through <see cref="Notify"/> only.</summary>
    public event EventHandler<BoardNotification>? Notified;

    private void Notify(string message, NoticeSeverity severity)
    {
        Notice = message;
        Notified?.Invoke(this, new BoardNotification(message, severity));
    }

    /// <summary>Live title/tag search. Empty matches everything.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
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
        Func<RepairPhraseViewModel, bool>? showRepairDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<SetupWizardViewModel>? showSetupWizard = null,
        Action<SettingsWindowViewModel>? showSettings = null,
        Func<string?>? pickExportPath = null,
        Func<(string Path, ImportMode Mode)?>? pickImportFile = null,
        Action? confirmAndRestart = null,
        Action<string>? showError = null,
        Action<string>? showSettingsInfo = null,
        Action? showRecorder = null)
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
        _showRepairDialog = showRepairDialog ?? (_ => false); // default: cancel (unit tests opt in)
        _showManageCategories = showManageCategories ?? (_ => { }); // default: no-op (unit tests)
        _showSetupWizard = showSetupWizard ?? (_ => { });  // default: no-op (unit tests)
        _showSettings = showSettings ?? (_ => { });        // default: no-op (unit tests)
        _pickExportPath = pickExportPath ?? (() => null);  // default: cancelled (unit tests)
        _pickImportFile = pickImportFile ?? (() => null);  // default: cancelled (unit tests)
        _confirmAndRestart = confirmAndRestart ?? (() => { }); // default: no-op (unit tests)
        _showError = showError ?? (_ => { });              // default: no-op (unit tests)
        _showSettingsInfo = showSettingsInfo ?? (_ => { }); // default: no-op (unit tests)
        _showRecorder = showRecorder ?? (() => { });       // default: no-op (unit tests)
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
            OnPropertyChanged(nameof(CategoryIsEmpty));
            OnPropertyChanged(nameof(SearchNoMatch));
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

    /// <summary>The idle "Record" button shows only when not recording, not mid-Stop, and no take
    /// is pending.</summary>
    public bool ShowRecordButton => !IsRecording && !IsProcessing && !HasPendingTake;

    /// <summary>The filtered, ordered view of <see cref="Phrases"/> the grid binds to.</summary>
    public ICollectionView PhrasesView { get; }

    /// <summary>"All categories" followed by the real categories — the filter dropdown's items. Rebuilt
    /// after the category manager runs, since categories may have been added/renamed/deleted.</summary>
    public IReadOnlyList<Category> CategoryFilterOptions { get; private set; }

    /// <summary>True when the board has no phrases at all — the view shows a first-run welcome card.</summary>
    public bool IsEmpty => Phrases.Count == 0;

    /// <summary>Inverse of <see cref="IsEmpty"/>.</summary>
    public bool HasPhrases => !IsEmpty;

    /// <summary>Phrases exist and a search is active, but it matches nothing — a distinct "no
    /// matches" state, separate from the first-run welcome (<see cref="IsEmpty"/>) and from
    /// <see cref="CategoryIsEmpty"/> (which owns the case with no search text). Mutually exclusive
    /// with CategoryIsEmpty by construction: this requires SearchText non-blank, that requires it
    /// blank.</summary>
    public bool SearchNoMatch => HasPhrases && !string.IsNullOrWhiteSpace(SearchText) && PhrasesView.IsEmpty;

    /// <summary>True once the operator has typed something into the search box — drives the inline
    /// Clear-search button next to the box itself.</summary>
    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);

    /// <summary>At least one phrase is visible under the current filter — the grid binds to this.</summary>
    public bool HasMatches => HasPhrases && !PhrasesView.IsEmpty;

    /// <summary>True when a specific category is selected, no search is active, and that category
    /// has no phrases at all — the CTA card offers to record straight into it. Mutually exclusive
    /// with the search-driven no-match state (Task 3): this one requires blank search text.</summary>
    public bool CategoryIsEmpty => HasPhrases
        && !string.IsNullOrEmpty(EffectiveCategoryId)
        && string.IsNullOrWhiteSpace(SearchText)
        && !Phrases.Any(p => p.Entry.CategoryId == EffectiveCategoryId);

    private string? EffectiveCategoryId =>
        string.IsNullOrEmpty(SelectedCategoryFilter?.Id) ? null : SelectedCategoryFilter.Id;

    /// <summary>Title/category/tags to apply to the next take <see cref="SaveTake"/> creates —
    /// set by <see cref="RecordIntoCategory"/> (category only) or the repair dialog's Re-record
    /// path (title, category and tags), and always cleared after Save or Discard so it can never
    /// leak into an unrelated future save. Also cleared by every path in <see cref="StartRecording"/>
    /// and <see cref="StopRecording"/> that ends without a <see cref="PendingTake"/> (failed start,
    /// no signal, or a mid-stop exception) — none of those paths ever reach Save/Discard, so this
    /// is the only place left to stop a stash from surviving into an unrelated future recording.</summary>
    private (string? Title, string? CategoryId, IReadOnlyList<string>? Tags) _pendingMetadata;

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

    /// <summary>Clear the search box — used by the inline Clear button and the search-no-match
    /// card's Clear-search button.</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = "";

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
        OnPropertyChanged(nameof(CategoryIsEmpty));
        OnPropertyChanged(nameof(SearchNoMatch));
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
    /// button still opens its right-click menu when the engine is stopped or the audio is missing.
    /// A broken phrase opens the repair dialog instead of playing.</summary>
    [RelayCommand]
    private async Task Play(PhraseItemViewModel? item)
    {
        if (item is null)
            return;

        if (item.IsBroken)
        {
            var repair = new RepairPhraseViewModel(item.Entry);
            if (_showRepairDialog(repair) && repair.Choice is { } choice)
            {
                _library.DeleteEntry(item.Entry);
                Phrases.Remove(item);

                if (choice == RepairChoice.ReRecord)
                {
                    _pendingMetadata = (item.Entry.Title, item.Entry.CategoryId, item.Entry.Tags);
                    await StartRecording();
                }
                else
                {
                    Deleted?.Invoke(this, item.Title);
                }
            }
            return;
        }

        if (_playback.State != EngineState.Live)
        {
            Notify("Start the engine (and be ON AIR) to play to the call.", NoticeSeverity.Warning);
            return;
        }

        Notice = null;
        _playback.PlayEntry(item.Entry);
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
                _onUiThread(() => Notify(error, NoticeSeverity.Error));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Corrupt WAV, no output device, COM failure — surface it instead of crashing the app.
            _onUiThread(() => Notify("Could not play the preview — check the playback device and try again.", NoticeSeverity.Error));
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

    /// <summary>Start a take. TryStartRecording blocks up to 2 s waiting for OFF AIR (and opens a
    /// capture device), so it runs off the UI thread — same rule as <see cref="PreviewTake"/>.</summary>
    [RelayCommand]
    private async Task StartRecording()
    {
        // A take is already in progress or waiting to be saved — starting another would silently
        // overwrite it. Just show the recorder so the operator can finish (save/discard) first.
        if (!ShowRecordButton)
        {
            _pendingMetadata = default; // a Re-record/CTA stash must not misfile the waiting take
            _showRecorder();
            return;
        }

        Notice = null;
        try
        {
            var started = await Task.Run(() => _recorder.TryStartRecording());
            _onUiThread(() =>
            {
                if (started)
                {
                    IsRecording = true;
                }
                else
                {
                    // No take will ever be created for this attempt (StopRecording never runs) — a
                    // stash made before this call (RecordIntoCategory / repair dialog Re-record)
                    // must not survive to misfile the operator's next, unrelated recording.
                    _pendingMetadata = default;
                    Notify("Press Start to go Live before recording.", NoticeSeverity.Warning);
                }
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Same reasoning as above: the mic failed, so no take — and no pending stash — exists.
            _onUiThread(() =>
            {
                _pendingMetadata = default;
                Notify("Could not start recording — check the microphone and try again.", NoticeSeverity.Error);
            });
        }

        // Open the recorder window last — the modal blocks here until it closes, and by now the
        // state it must show is set either way: Recording…, or the Notice explaining the failure.
        // The view no-ops if the recorder is already open (Record clicked inside it).
        _showRecorder();
    }

    /// <summary>Record straight into the currently selected (empty) category — the category-empty
    /// CTA's button. Reuses StartRecording exactly as clicking the normal Record button would;
    /// only the pending-category stash differs.</summary>
    [RelayCommand]
    private async Task RecordIntoCategory()
    {
        _pendingMetadata = (_pendingMetadata.Title, EffectiveCategoryId, _pendingMetadata.Tags);
        await StartRecording();
    }

    /// <summary>Stop the take. StopRecording trims/loudness-matches the audio and waits for the
    /// engine to go back on air, so it runs off the UI thread too. IsProcessing covers the whole
    /// window so the idle Record button never flashes back before the pending-take bar appears.</summary>
    [RelayCommand]
    private async Task StopRecording()
    {
        IsRecording = false;
        IsProcessing = true;
        try
        {
            var take = await Task.Run(() => _recorder.StopRecording());
            _onUiThread(() =>
            {
                if (take is { HasSignal: true })
                {
                    PendingTake = take;
                    // A repair-dialog Re-record (or RecordIntoCategory) may have stashed the
                    // original title before recording started; fall back to a timestamp otherwise.
                    NewTitle = _pendingMetadata.Title ?? $"Take {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    Notice = null;
                }
                else
                {
                    // No take was created — SaveTake/DiscardTake will never run for this attempt,
                    // so any stash from a repair-dialog Re-record or RecordIntoCategory must be
                    // cleared here or it would leak into the operator's next, unrelated recording.
                    PendingTake = null;
                    _pendingMetadata = default;
                    Notify("No signal — nothing recorded.", NoticeSeverity.Warning);
                }
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _onUiThread(() =>
            {
                // Same reasoning as the no-signal branch above: no take, so no stash may survive.
                PendingTake = null;
                _pendingMetadata = default;
                Notify("Could not finish the recording — the take was lost.", NoticeSeverity.Error);
            });
        }
        finally
        {
            IsProcessing = false;
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

        Notify("Previewing…", NoticeSeverity.Info);
        try
        {
            var error = await Task.Run(() => _recorder.Preview(take.Samples, take.GainDb));
            if (error is not null)
                _onUiThread(() => Notify(error, NoticeSeverity.Error));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _onUiThread(() => Notify("Could not play the preview — check the playback device and try again.", NoticeSeverity.Error));
        }
    }

    /// <summary>True once there's a non-blank title to save — every other recording command
    /// already guards its own failure path (M1/M2); this was the one gap the 2026-07-04 review
    /// flagged (M15) as still open: no CanExecute (an empty title was silently accepted) and no
    /// catch (a disk-full write would bubble to the global handler's generic dialog instead of
    /// this section's friendly notification toast).</summary>
    private bool CanSaveTake() => !string.IsNullOrWhiteSpace(NewTitle);

    [RelayCommand(CanExecute = nameof(CanSaveTake))]
    private void SaveTake()
    {
        if (PendingTake is not { } take)
            return;

        PhraseEntry entry;
        try
        {
            entry = _recorder.SaveTake(take, NewTitle);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nothing persisted yet — keep PendingTake (and the pending metadata) set so the
            // operator can retry Save or Discard instead of silently losing the recording.
            Notify("Could not save the recording — check disk space and try again.", NoticeSeverity.Error);
            return;
        }

        // The recorder save above already succeeded (WAV written, entry created). A failure here
        // must not send the operator back to Save — that would call _recorder.SaveTake again and
        // create a duplicate entry. Downgrade to a warning instead; the take is still saved.
        try
        {
            if (_pendingMetadata.CategoryId is { } categoryId)
                entry = _library.SetPhraseCategory(entry.Id, categoryId) ?? entry;
            if (_pendingMetadata.Tags is { } tags)
                entry = _library.SetPhraseTags(entry.Id, tags) ?? entry;
            Notice = null; // the "Saved" feedback is now a toast (see Saved)
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Notify("Saved, but could not apply the category/tags — edit it manually.", NoticeSeverity.Warning);
        }
        _pendingMetadata = default;

        PendingTake = null;
        Phrases.Add(new PhraseItemViewModel(entry)); // appears on the board immediately
        ApplyColors(); // tint the new tile (falls back to its default category colour)
        Saved?.Invoke(this, entry.Title);
    }

    [RelayCommand]
    private void DiscardTake()
    {
        PendingTake = null;
        _pendingMetadata = default;
        Notify("Take discarded.", NoticeSeverity.Info);
    }
}
