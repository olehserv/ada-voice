# Interaction-State Gaps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close five interaction-state gaps on the existing Board — a repair dialog for broken
phrases, a category-empty CTA, a search Clear button with query echo, a Recorder "Processing…"
state with a hardened `SaveTake`, and a cosmetic wizard per-row spinner — without any new host
seam or new audio capability.

**Architecture:** All five items are `BoardViewModel`/View changes (plus one cosmetic
View-only change in the setup wizard). Two features (category-empty CTA, repair dialog) share a
single pending-metadata field on `BoardViewModel` that `SaveTake` applies to whatever it creates
next — this is the only new piece of shared state the plan introduces. No new
`ISetupHost`/`ISettingsHost`/`ILibraryHost`/`IRecorderHost` members.

**Tech Stack:** .NET 10 WPF (`net10.0-windows`), CommunityToolkit.Mvvm, WPF-UI 4.3.0, xUnit.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-04-interaction-state-gaps-design.md` — read it before
  starting; every task below implements one piece of it.
- The recorder's live level meter and live "mic dropped mid-recording" detection are **out of
  scope** — do not add any capture-stream polling. That belongs in a future slice alongside the
  Settings window's Devices group.
- **Any new `<Run Text="{Binding ...}">` must include `Mode=OneWay` explicitly.** `Run.Text`
  defaults to `TwoWay` in WPF (unlike `TextBlock.Text`, which defaults to `OneWay`), and binding a
  `TwoWay` `Run.Text` to a property with no public setter throws `InvalidOperationException` at
  runtime. This exact bug was just fixed in the Settings window's `LastBackupDate` binding (commit
  `05d2f26`). `Category.Name` is `init`-only, so any `Run` bound to `SelectedCategoryFilter.Name`
  needs this — the same applies to any other new `Run` this plan adds.
- Follow the existing flat file layout: view-models directly under `src/AdaVoice.App/ViewModels/`,
  windows/dialogs directly under `src/AdaVoice.App/`, converters in the existing
  `src/AdaVoice.App/Converters.cs` — no new subfolders.
- `BoardViewModel`'s recording commands (`StartRecording`, `StopRecording`, `PreviewTake`) are
  already async off the UI thread with `catch (Exception ex) when (ex is not
  OutOfMemoryException)` → `Notice` guards (fixed 2026-07-04, review items M1/M2). Match that exact
  catch clause and `Notice`-based error style for any new catch this plan adds.
- Run the full suite (`dotnet test --nologo` from the repo root) after every task; all prior tests
  must stay green. As of this plan's start: 335 tests (72 Core + 97 Audio + 8 Wasapi + 5 Host +
  153 App).

---

### Task 1: Recorder Processing state + `SaveTake` guard

**Files:**
- Modify: `src/AdaVoice.App/ViewModels/BoardViewModel.cs`
- Modify: `src/AdaVoice.App/MainWindow.xaml`
- Modify: `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`
- Modify: `tests/AdaVoice.App.Tests/FakePlaybackHost.cs`

**Interfaces:**
- Consumes: `IRecorderHost.StopRecording()` (existing), `IRecorderHost.SaveTake(RecordingResult,
  string)` (existing).
- Produces: `BoardViewModel.IsProcessing` (bool, observable) — true from the moment `Stop` is
  clicked until the pending take is ready or the attempt fails; `ShowRecordButton` now also
  excludes it. `SaveTakeCommand`'s `CanExecute` is false for a blank/whitespace `NewTitle`. Task 2
  extends `SaveTake`'s body (adds pending-metadata application) but does not change this task's
  `IsProcessing`/`CanExecute` behavior.

- [ ] **Step 1: Add the `SaveTakeThrows` knob to the fake**

In `tests/AdaVoice.App.Tests/FakePlaybackHost.cs`, add `using System.IO;` to the usings at the top,
then change:

```csharp
    public PhraseEntry SaveTake(RecordingResult result, string title)
    {
        Calls.Add("SaveTake");
        SavedTitle = title;
        // Mirror EngineHost: a new take lands in the default category.
        var entry = new PhraseEntry { Id = "p-saved", Title = title, CategoryId = Category.DefaultId };
        _phrases.Add(entry);
        return entry;
    }
```

to:

```csharp
    public bool SaveTakeThrows { get; set; }

    public PhraseEntry SaveTake(RecordingResult result, string title)
    {
        Calls.Add("SaveTake");
        if (SaveTakeThrows)
            throw new IOException("disk full (simulated)");
        SavedTitle = title;
        // Mirror EngineHost: a new take lands in the default category.
        var entry = new PhraseEntry { Id = "p-saved", Title = title, CategoryId = Category.DefaultId };
        _phrases.Add(entry);
        return entry;
    }
```

- [ ] **Step 2: Write the failing tests**

In `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`, add after
`Save_take_saves_with_the_title_and_refreshes_the_board` (around line 255):

```csharp
    [Fact]
    public void Save_take_is_disabled_when_the_title_is_blank()
    {
        var board = NewBoard(new FakePlaybackHost());
        board.PendingTake = Take();
        board.NewTitle = "   ";

        Assert.False(board.SaveTakeCommand.CanExecute(null));
    }

    [Fact]
    public void Save_take_becomes_enabled_once_a_title_is_typed()
    {
        var board = NewBoard(new FakePlaybackHost());
        board.PendingTake = Take();
        board.NewTitle = "";
        Assert.False(board.SaveTakeCommand.CanExecute(null));

        board.NewTitle = "Greeting";

        Assert.True(board.SaveTakeCommand.CanExecute(null));
    }

    [Fact]
    public void Save_take_failure_keeps_the_pending_take_and_shows_a_notice()
    {
        var host = new FakePlaybackHost { SaveTakeThrows = true };
        var board = NewBoard(host);
        board.PendingTake = Take();
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        Assert.True(board.HasPendingTake); // not lost — the operator can retry Save or Discard
        Assert.NotNull(board.Notice);
    }

    // IsProcessing is set synchronously before the first `await` inside StopRecording, so it is
    // already true the instant ExecuteAsync returns its (still-running) Task — this is not a race,
    // it's how C# async methods run their pre-await prefix on the caller's thread.
    [Fact]
    public async Task Stop_recording_reports_processing_until_the_take_is_ready()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);

        var stopTask = board.StopRecordingCommand.ExecuteAsync(null);
        Assert.True(board.IsProcessing);
        Assert.False(board.ShowRecordButton); // the idle Record button must not flash back

        await stopTask;

        Assert.False(board.IsProcessing);
        Assert.True(board.HasPendingTake);
    }

    [Fact]
    public async Task Stop_recording_clears_processing_even_on_failure()
    {
        var host = new FakePlaybackHost { NextStopResult = null }; // StopRecording throws below
        var board = NewBoard(host);
        host.ThrowOnStopRecording = true;

        await board.StopRecordingCommand.ExecuteAsync(null);

        Assert.False(board.IsProcessing);
    }
```

- [ ] **Step 3: Add the `ThrowOnStopRecording` knob the last test needs**

In `tests/AdaVoice.App.Tests/FakePlaybackHost.cs`, change:

```csharp
    public RecordingResult? StopRecording()
    {
        Calls.Add("StopRecording");
        return NextStopResult;
    }
```

to:

```csharp
    public bool ThrowOnStopRecording { get; set; }

    public RecordingResult? StopRecording()
    {
        Calls.Add("StopRecording");
        if (ThrowOnStopRecording)
            throw new InvalidOperationException("engine vanished (simulated)");
        return NextStopResult;
    }
```

- [ ] **Step 4: Run the new tests to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "Save_take_is_disabled_when_the_title_is_blank|Save_take_becomes_enabled_once_a_title_is_typed|Save_take_failure_keeps_the_pending_take_and_shows_a_notice|Stop_recording_reports_processing_until_the_take_is_ready|Stop_recording_clears_processing_even_on_failure"`
Expected: FAIL to compile — `IsProcessing`/`SaveTakeCommand.CanExecute` behavior and
`SaveTakeThrows`/`ThrowOnStopRecording` don't exist on the view-model side yet (the fake knobs from
Steps 1/3 will compile fine on their own).

- [ ] **Step 5: Add `IsProcessing` and wire `ShowRecordButton`**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, change:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingTake))]
    [NotifyPropertyChangedFor(nameof(ShowRecordButton))]
    [NotifyPropertyChangedFor(nameof(PendingTakeDurationLabel))]
    private RecordingResult? _pendingTake;

    [ObservableProperty]
    private string _newTitle = "";
```

to:

```csharp
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
```

Then change:

```csharp
    /// <summary>The idle "Record" button shows only when not recording and no take is pending.</summary>
    public bool ShowRecordButton => !IsRecording && !HasPendingTake;
```

to:

```csharp
    /// <summary>The idle "Record" button shows only when not recording, not mid-Stop, and no take
    /// is pending.</summary>
    public bool ShowRecordButton => !IsRecording && !IsProcessing && !HasPendingTake;
```

- [ ] **Step 6: Set `IsProcessing` around `StopRecording`'s async work**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, change:

```csharp
    /// <summary>Stop the take. StopRecording trims/loudness-matches the audio and waits for the
    /// engine to go back on air, so it runs off the UI thread too.</summary>
    [RelayCommand]
    private async Task StopRecording()
    {
        IsRecording = false;
        try
        {
            var take = await Task.Run(() => _recorder.StopRecording());
            _onUiThread(() =>
            {
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
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _onUiThread(() =>
            {
                PendingTake = null;
                Notice = "Could not finish the recording — the take was lost.";
            });
        }
    }
```

to:

```csharp
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
                    NewTitle = $"Take {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    Notice = null;
                }
                else
                {
                    PendingTake = null;
                    Notice = "No signal — nothing recorded.";
                }
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _onUiThread(() =>
            {
                PendingTake = null;
                Notice = "Could not finish the recording — the take was lost.";
            });
        }
        finally
        {
            IsProcessing = false;
        }
    }
```

- [ ] **Step 7: Add the `CanExecute` guard and try/catch to `SaveTake`**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, change:

```csharp
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
```

to:

```csharp
    /// <summary>True once there's a non-blank title to save — every other recording command
    /// already guards its own failure path (M1/M2); this was the one gap the 2026-07-04 review
    /// flagged (M15) as still open: no CanExecute (an empty title was silently accepted) and no
    /// catch (a disk-full write would bubble to the global handler's generic dialog instead of
    /// this section's friendly inline Notice).</summary>
    private bool CanSaveTake() => !string.IsNullOrWhiteSpace(NewTitle);

    [RelayCommand(CanExecute = nameof(CanSaveTake))]
    private void SaveTake()
    {
        if (PendingTake is not { } take)
            return;

        try
        {
            var entry = _recorder.SaveTake(take, NewTitle);
            PendingTake = null;
            Notice = null; // the "Saved" feedback is now a toast (see Saved)
            Phrases.Add(new PhraseItemViewModel(entry)); // appears on the board immediately
            ApplyColors(); // tint the new tile (falls back to its default category colour)
            Saved?.Invoke(this, entry.Title);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Keep PendingTake set so the operator can retry Save or Discard instead of silently
            // losing the recording.
            Notice = "Could not save the recording — check disk space and try again.";
        }
    }
```

- [ ] **Step 8: Add the "Processing…" slot to the record-area XAML**

In `src/AdaVoice.App/MainWindow.xaml`, change:

```xml
                    <!-- Recording -->
                    <StackPanel Orientation="Horizontal"
                                Visibility="{Binding IsRecording, Converter={StaticResource BoolToVis}}">
                        <TextBlock Text="Recording…" VerticalAlignment="Center" FontWeight="SemiBold" />
                        <ui:Button Content="Stop" Appearance="Secondary" Command="{Binding StopRecordingCommand}" Margin="12,0,0,0" />
                    </StackPanel>

                    <!-- A take is waiting to be saved -->
```

to:

```xml
                    <!-- Recording -->
                    <StackPanel Orientation="Horizontal"
                                Visibility="{Binding IsRecording, Converter={StaticResource BoolToVis}}">
                        <TextBlock Text="Recording…" VerticalAlignment="Center" FontWeight="SemiBold" />
                        <ui:Button Content="Stop" Appearance="Secondary" Command="{Binding StopRecordingCommand}" Margin="12,0,0,0" />
                    </StackPanel>

                    <!-- Bridges the gap between Stop and the pending-take bar appearing, so the
                         idle Record button never flashes back in between. -->
                    <TextBlock Text="Processing…" VerticalAlignment="Center" FontWeight="SemiBold"
                               Visibility="{Binding IsProcessing, Converter={StaticResource BoolToVis}}" />

                    <!-- A take is waiting to be saved -->
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "Save_take_is_disabled_when_the_title_is_blank|Save_take_becomes_enabled_once_a_title_is_typed|Save_take_failure_keeps_the_pending_take_and_shows_a_notice|Stop_recording_reports_processing_until_the_take_is_ready|Stop_recording_clears_processing_even_on_failure"`
Expected: PASS, 5 tests.

- [ ] **Step 10: Run the full App suite and build to verify no regressions**

Run: `dotnet build src/AdaVoice.App --nologo && dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` and all tests pass (153 prior + 5 new = 158).

- [ ] **Step 11: Commit**

```bash
git add src/AdaVoice.App/ViewModels/BoardViewModel.cs src/AdaVoice.App/MainWindow.xaml tests/AdaVoice.App.Tests/BoardViewModelTests.cs tests/AdaVoice.App.Tests/FakePlaybackHost.cs
git commit -m "feat(app): Recorder Processing state; SaveTake CanExecute + disk-full guard (M15)"
```

---

### Task 2: Category-empty CTA

**Files:**
- Modify: `src/AdaVoice.App/ViewModels/BoardViewModel.cs`
- Modify: `src/AdaVoice.App/MainWindow.xaml`
- Modify: `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`

**Interfaces:**
- Consumes: `ILibraryHost.SetPhraseCategory(string, string)` (existing), `StartRecording()` (Task 1,
  unchanged signature — called directly, not through its command).
- Produces: `BoardViewModel.CategoryIsEmpty` (bool, observable); `RecordIntoCategoryCommand`;
  `_pendingMetadata` — a private `(string? CategoryId, IReadOnlyList<string>? Tags)` field that
  `SaveTake` applies to the newly created entry and always clears afterward. Task 3 does not touch
  this field. Task 4 (repair dialog) sets both halves of it for its Re-record path.

- [ ] **Step 1: Write the failing tests**

In `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`, add after
`Category_filter_limits_to_the_chosen_category` (around line 617):

```csharp
    [Fact]
    public void Category_is_empty_when_selected_category_has_no_phrases_and_search_is_blank()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }, new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        var board = NewBoard(host);

        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");

        Assert.True(board.CategoryIsEmpty);
    }

    [Fact]
    public void Category_is_not_empty_when_it_has_a_phrase()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        var board = NewBoard(host);

        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-1");

        Assert.False(board.CategoryIsEmpty);
    }

    [Fact]
    public void Category_is_not_reported_empty_while_search_text_is_active()
    {
        // Search-no-match (Task 3) owns this case instead — the two states are mutually exclusive
        // by construction (one requires blank search text, the other requires non-blank).
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        var board = NewBoard(host);
        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");

        board.SearchText = "hello";

        Assert.False(board.CategoryIsEmpty);
    }

    [Fact]
    public void Category_is_not_empty_when_all_categories_is_selected()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi" }] };
        var board = NewBoard(host);

        Assert.False(board.CategoryIsEmpty); // default filter is "All categories"
    }

    [Fact]
    public async Task Record_into_category_starts_recording_like_the_normal_Record_button()
    {
        var host = new FakePlaybackHost { CanRecord = true, Categories = [new Category { Id = "c-2", Name = "Closers" }] };
        var board = NewBoard(host);
        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");

        await board.RecordIntoCategoryCommand.ExecuteAsync(null);

        Assert.True(board.IsRecording);
    }

    [Fact]
    public async Task Record_into_category_applies_the_category_to_the_saved_take()
    {
        var host = new FakePlaybackHost { CanRecord = true, Categories = [new Category { Id = "c-2", Name = "Closers" }] };
        var board = NewBoard(host);
        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");

        await board.RecordIntoCategoryCommand.ExecuteAsync(null);
        board.PendingTake = Take();
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Greeting");
        Assert.Equal("c-2", saved.CategoryId);
    }

    [Fact]
    public async Task Discarding_a_take_clears_any_pending_category()
    {
        var host = new FakePlaybackHost { CanRecord = true, Categories = [new Category { Id = "c-2", Name = "Closers" }] };
        var board = NewBoard(host);
        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");
        await board.RecordIntoCategoryCommand.ExecuteAsync(null);
        board.DiscardTakeCommand.Execute(null);

        // A later, unrelated save must NOT pick up the stale pending category.
        board.PendingTake = Take();
        board.NewTitle = "Unrelated";
        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Unrelated");
        Assert.Equal(Category.DefaultId, saved.CategoryId);
    }
```

Also update the one existing test whose premise changes under this task's `NoMatches` edit — in
`Editing_a_phrase_out_of_the_active_filter_hides_it_from_the_view` (around line 357), change the
final line:

```csharp
        Assert.True(board.NoMatches);
```

to:

```csharp
        // The category is now genuinely empty (not just search-filtered) — CategoryIsEmpty owns
        // this case; NoMatches is deliberately false here after this task's change.
        Assert.True(board.CategoryIsEmpty);
        Assert.False(board.NoMatches);
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "Category_is_empty_when_selected_category_has_no_phrases_and_search_is_blank|Category_is_not_empty_when_it_has_a_phrase|Category_is_not_reported_empty_while_search_text_is_active|Category_is_not_empty_when_all_categories_is_selected|Record_into_category_starts_recording_like_the_normal_Record_button|Record_into_category_applies_the_category_to_the_saved_take|Discarding_a_take_clears_any_pending_category"`
Expected: FAIL to compile — `CategoryIsEmpty`/`RecordIntoCategoryCommand` do not exist yet.

- [ ] **Step 3: Add `CategoryIsEmpty`, tighten `NoMatches`, and add the pending-metadata field**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, change:

```csharp
    /// <summary>Phrases exist but the current search/filter hides them all — a distinct "no matches"
    /// state, separate from the first-run welcome (<see cref="IsEmpty"/>).</summary>
    public bool NoMatches => HasPhrases && PhrasesView.IsEmpty;

    /// <summary>At least one phrase is visible under the current filter — the grid binds to this.</summary>
    public bool HasMatches => HasPhrases && !PhrasesView.IsEmpty;

    private string? EffectiveCategoryId =>
        string.IsNullOrEmpty(SelectedCategoryFilter?.Id) ? null : SelectedCategoryFilter.Id;
```

to:

```csharp
    /// <summary>Phrases exist but the current search/filter hides them all, and it isn't because
    /// the selected category is itself empty (see <see cref="CategoryIsEmpty"/>) — a distinct "no
    /// matches" state, separate from the first-run welcome (<see cref="IsEmpty"/>). Task 3 narrows
    /// this further into a search-specific state; left as-is here since Task 2 only needs to carve
    /// the category-empty case out of it.</summary>
    public bool NoMatches => HasPhrases && PhrasesView.IsEmpty && !CategoryIsEmpty;

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

    /// <summary>Category and/or tags to apply to the next take <see cref="SaveTake"/> creates —
    /// set by <see cref="RecordIntoCategory"/> (category only) or the repair dialog's Re-record
    /// path (category and tags), and always cleared after Save or Discard so it can never leak
    /// into an unrelated future save.</summary>
    private (string? CategoryId, IReadOnlyList<string>? Tags) _pendingMetadata;
```

- [ ] **Step 4: Raise `CategoryIsEmpty` wherever `NoMatches`/`HasMatches` are already raised**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, change:

```csharp
        Phrases.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPhrases));
            OnPropertyChanged(nameof(NoMatches));
            OnPropertyChanged(nameof(HasMatches));
        };
```

to:

```csharp
        Phrases.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPhrases));
            OnPropertyChanged(nameof(CategoryIsEmpty));
            OnPropertyChanged(nameof(NoMatches));
            OnPropertyChanged(nameof(HasMatches));
        };
```

and change:

```csharp
    private void RefreshFilter()
    {
        PhrasesView.Refresh();
        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(HasMatches));
    }
```

to:

```csharp
    private void RefreshFilter()
    {
        PhrasesView.Refresh();
        OnPropertyChanged(nameof(CategoryIsEmpty));
        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(HasMatches));
    }
```

- [ ] **Step 5: Add `RecordIntoCategory` and apply the pending metadata in `SaveTake`**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, add after `StartRecording` (the method from
Task 1, right before `StopRecording`):

```csharp
    /// <summary>Record straight into the currently selected (empty) category — the category-empty
    /// CTA's button. Reuses StartRecording exactly as clicking the normal Record button would;
    /// only the pending-category stash differs.</summary>
    [RelayCommand]
    private async Task RecordIntoCategory()
    {
        _pendingMetadata = (EffectiveCategoryId, _pendingMetadata.Tags);
        await StartRecording();
    }
```

Then change `SaveTake` (from Task 1) from:

```csharp
    [RelayCommand(CanExecute = nameof(CanSaveTake))]
    private void SaveTake()
    {
        if (PendingTake is not { } take)
            return;

        try
        {
            var entry = _recorder.SaveTake(take, NewTitle);
            PendingTake = null;
            Notice = null; // the "Saved" feedback is now a toast (see Saved)
            Phrases.Add(new PhraseItemViewModel(entry)); // appears on the board immediately
            ApplyColors(); // tint the new tile (falls back to its default category colour)
            Saved?.Invoke(this, entry.Title);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Keep PendingTake set so the operator can retry Save or Discard instead of silently
            // losing the recording.
            Notice = "Could not save the recording — check disk space and try again.";
        }
    }
```

to:

```csharp
    [RelayCommand(CanExecute = nameof(CanSaveTake))]
    private void SaveTake()
    {
        if (PendingTake is not { } take)
            return;

        try
        {
            var entry = _recorder.SaveTake(take, NewTitle);
            if (_pendingMetadata.CategoryId is { } categoryId)
                entry = _library.SetPhraseCategory(entry.Id, categoryId) ?? entry;
            if (_pendingMetadata.Tags is { } tags)
                entry = _library.SetPhraseTags(entry.Id, tags) ?? entry;
            _pendingMetadata = default;

            PendingTake = null;
            Notice = null; // the "Saved" feedback is now a toast (see Saved)
            Phrases.Add(new PhraseItemViewModel(entry)); // appears on the board immediately
            ApplyColors(); // tint the new tile (falls back to its default category colour)
            Saved?.Invoke(this, entry.Title);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Keep PendingTake (and the pending metadata) set so the operator can retry Save or
            // Discard instead of silently losing the recording.
            Notice = "Could not save the recording — check disk space and try again.";
        }
    }
```

- [ ] **Step 6: Clear the pending metadata on Discard**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, change:

```csharp
    [RelayCommand]
    private void DiscardTake()
    {
        PendingTake = null;
        Notice = "Take discarded.";
    }
```

to:

```csharp
    [RelayCommand]
    private void DiscardTake()
    {
        PendingTake = null;
        _pendingMetadata = default;
        Notice = "Take discarded.";
    }
```

- [ ] **Step 7: Add the category-empty CTA card to the XAML**

In `src/AdaVoice.App/MainWindow.xaml`, add right after the first-run welcome `ui:Card` (after its
closing `</ui:Card>`, before the "Phrases exist, but the current search/filter..." comment):

```xml
        <!-- A specific category is selected and it genuinely has no phrases (search is blank) —
             distinct from the search-driven no-match card below. Remember: any new Run.Text bound
             to Category.Name needs Mode=OneWay (Name is init-only — see Global Constraints). -->
        <ui:Card VerticalAlignment="Center" HorizontalAlignment="Center" Padding="24" MaxWidth="320"
                 Visibility="{Binding CategoryIsEmpty, Converter={StaticResource BoolToVis}}">
            <StackPanel>
                <TextBlock HorizontalAlignment="Center"
                           FontSize="{StaticResource FontSize.SectionTitle}" FontWeight="SemiBold">
                    <Run Text="No phrases in " /><Run Text="{Binding SelectedCategoryFilter.Name, Mode=OneWay}" /><Run Text=" yet." />
                </TextBlock>
                <ui:Button Appearance="Primary" HorizontalAlignment="Center" Margin="0,12,0,0"
                           Command="{Binding RecordIntoCategoryCommand}">
                    <TextBlock><Run Text="Record into " /><Run Text="{Binding SelectedCategoryFilter.Name, Mode=OneWay}" /></TextBlock>
                </ui:Button>
            </StackPanel>
        </ui:Card>

```

- [ ] **Step 8: Run the new and updated tests to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "Category_is_empty_when_selected_category_has_no_phrases_and_search_is_blank|Category_is_not_empty_when_it_has_a_phrase|Category_is_not_reported_empty_while_search_text_is_active|Category_is_not_empty_when_all_categories_is_selected|Record_into_category_starts_recording_like_the_normal_Record_button|Record_into_category_applies_the_category_to_the_saved_take|Discarding_a_take_clears_any_pending_category|Editing_a_phrase_out_of_the_active_filter_hides_it_from_the_view"`
Expected: PASS, 8 tests.

- [ ] **Step 9: Run the full App suite and build to verify no regressions**

Run: `dotnet build src/AdaVoice.App --nologo && dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` and all tests pass (158 prior + 7 new = 165).

- [ ] **Step 10: Commit**

```bash
git add src/AdaVoice.App/ViewModels/BoardViewModel.cs src/AdaVoice.App/MainWindow.xaml tests/AdaVoice.App.Tests/BoardViewModelTests.cs
git commit -m "feat(app): category-empty CTA — record straight into an empty category"
```

---

### Task 3: Search Clear button + query echo

**Files:**
- Modify: `src/AdaVoice.App/ViewModels/BoardViewModel.cs`
- Modify: `src/AdaVoice.App/MainWindow.xaml`
- Modify: `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `BoardViewModel.SearchNoMatch` (bool, observable, replaces `NoMatches`);
  `BoardViewModel.HasSearchText` (bool, observable); `ClearSearchCommand`. No other task consumes
  these.

- [ ] **Step 1: Write the failing tests**

In `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`, first rename the three remaining `NoMatches`
assertions to `SearchNoMatch` (all three are genuinely search-driven, unlike the one Task 2 already
fixed):

In `Search_filters_by_title_case_insensitively` (around line 579), change:

```csharp
        Assert.False(board.NoMatches);
```

to:

```csharp
        Assert.False(board.SearchNoMatch);
```

In `No_matches_state_is_distinct_from_first_run_empty` (around line 620), change:

```csharp
    [Fact]
    public void No_matches_state_is_distinct_from_first_run_empty()
    {
        var board = NewBoard(new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1", Title = "Hello" }] });
        board.SearchText = "zzz";

        Assert.True(board.NoMatches);  // phrases exist, none match
        Assert.False(board.IsEmpty);   // not the first-run welcome
        Assert.False(board.HasMatches);

        var firstRun = NewBoard(new FakePlaybackHost());
        Assert.True(firstRun.IsEmpty);     // first-run welcome
        Assert.False(firstRun.NoMatches);  // not a "no matches" result
    }
```

to:

```csharp
    [Fact]
    public void No_matches_state_is_distinct_from_first_run_empty()
    {
        var board = NewBoard(new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1", Title = "Hello" }] });
        board.SearchText = "zzz";

        Assert.True(board.SearchNoMatch);  // phrases exist, none match
        Assert.False(board.IsEmpty);   // not the first-run welcome
        Assert.False(board.HasMatches);

        var firstRun = NewBoard(new FakePlaybackHost());
        Assert.True(firstRun.IsEmpty);     // first-run welcome
        Assert.False(firstRun.SearchNoMatch);  // not a "no matches" result
    }
```

Then add after that test:

```csharp
    [Fact]
    public void Search_no_match_is_true_even_with_a_category_selected()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hello", CategoryId = "c-1" }],
        };
        var board = NewBoard(host);
        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-1");

        board.SearchText = "zzz";

        Assert.True(board.SearchNoMatch);
        Assert.False(board.CategoryIsEmpty); // the category itself isn't empty — the search is
    }

    [Fact]
    public void Has_search_text_reflects_whether_a_query_is_active()
    {
        var board = NewBoard(new FakePlaybackHost());
        Assert.False(board.HasSearchText);

        board.SearchText = "x";

        Assert.True(board.HasSearchText);
    }

    [Fact]
    public void Clear_search_resets_the_search_text()
    {
        var board = NewBoard(new FakePlaybackHost());
        board.SearchText = "xyz";

        board.ClearSearchCommand.Execute(null);

        Assert.Equal("", board.SearchText);
        Assert.False(board.HasSearchText);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "Search_filters_by_title_case_insensitively|No_matches_state_is_distinct_from_first_run_empty|Search_no_match_is_true_even_with_a_category_selected|Has_search_text_reflects_whether_a_query_is_active|Clear_search_resets_the_search_text"`
Expected: FAIL to compile — `SearchNoMatch`/`HasSearchText`/`ClearSearchCommand` don't exist yet.

- [ ] **Step 3: Replace `NoMatches` with `SearchNoMatch`, add `HasSearchText` and `ClearSearch`**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, change:

```csharp
    /// <summary>Live title/tag search. Empty matches everything.</summary>
    [ObservableProperty]
    private string _searchText = "";
```

to:

```csharp
    /// <summary>Live title/tag search. Empty matches everything.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = "";
```

Then change:

```csharp
    /// <summary>Phrases exist but the current search/filter hides them all, and it isn't because
    /// the selected category is itself empty (see <see cref="CategoryIsEmpty"/>) — a distinct "no
    /// matches" state, separate from the first-run welcome (<see cref="IsEmpty"/>). Task 3 narrows
    /// this further into a search-specific state; left as-is here since Task 2 only needs to carve
    /// the category-empty case out of it.</summary>
    public bool NoMatches => HasPhrases && PhrasesView.IsEmpty && !CategoryIsEmpty;
```

to:

```csharp
    /// <summary>Phrases exist and a search is active, but it matches nothing — a distinct "no
    /// matches" state, separate from the first-run welcome (<see cref="IsEmpty"/>) and from
    /// <see cref="CategoryIsEmpty"/> (which owns the case with no search text). Mutually exclusive
    /// with CategoryIsEmpty by construction: this requires SearchText non-blank, that requires it
    /// blank.</summary>
    public bool SearchNoMatch => HasPhrases && !string.IsNullOrWhiteSpace(SearchText) && PhrasesView.IsEmpty;

    /// <summary>True once the operator has typed something into the search box — drives the inline
    /// Clear-search button next to the box itself.</summary>
    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);
```

Then update the two places that raised `NoMatches` — change:

```csharp
        Phrases.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPhrases));
            OnPropertyChanged(nameof(CategoryIsEmpty));
            OnPropertyChanged(nameof(NoMatches));
            OnPropertyChanged(nameof(HasMatches));
        };
```

to:

```csharp
        Phrases.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPhrases));
            OnPropertyChanged(nameof(CategoryIsEmpty));
            OnPropertyChanged(nameof(SearchNoMatch));
            OnPropertyChanged(nameof(HasMatches));
        };
```

and change:

```csharp
    private void RefreshFilter()
    {
        PhrasesView.Refresh();
        OnPropertyChanged(nameof(CategoryIsEmpty));
        OnPropertyChanged(nameof(NoMatches));
        OnPropertyChanged(nameof(HasMatches));
    }
```

to:

```csharp
    private void RefreshFilter()
    {
        PhrasesView.Refresh();
        OnPropertyChanged(nameof(CategoryIsEmpty));
        OnPropertyChanged(nameof(SearchNoMatch));
        OnPropertyChanged(nameof(HasMatches));
    }
```

Finally, add the command next to `ManageCategories`:

```csharp
    /// <summary>Clear the search box — used by the inline Clear button and the search-no-match
    /// card's Clear-search button.</summary>
    [RelayCommand]
    private void ClearSearch() => SearchText = "";
```

- [ ] **Step 4: Update the search box + no-match card XAML**

In `src/AdaVoice.App/MainWindow.xaml`, change:

```xml
        <Grid Grid.Row="0" Margin="0,0,0,8"
              Visibility="{Binding HasPhrases, Converter={StaticResource BoolToVis}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <ui:TextBox Grid.Column="0" PlaceholderText="Search title or tags…"
                        Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />
            <ComboBox Grid.Column="1" Margin="8,0,0,0" MinWidth="150" VerticalAlignment="Center"
                      ItemsSource="{Binding CategoryFilterOptions}"
                      SelectedItem="{Binding SelectedCategoryFilter}"
                      DisplayMemberPath="Name" />
            <ui:Button Grid.Column="2" Margin="8,0,0,0" Appearance="Secondary"
                       Content="Categories…" Command="{Binding ManageCategoriesCommand}"
                       ToolTip="Add, rename, or delete categories" />
        </Grid>
```

to:

```xml
        <Grid Grid.Row="0" Margin="0,0,0,8"
              Visibility="{Binding HasPhrases, Converter={StaticResource BoolToVis}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <ui:TextBox Grid.Column="0" PlaceholderText="Search title or tags…"
                        Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />
            <ui:Button Grid.Column="1" Margin="4,0,0,0" Appearance="Secondary" Content="✕"
                       Command="{Binding ClearSearchCommand}" ToolTip="Clear search"
                       Visibility="{Binding HasSearchText, Converter={StaticResource BoolToVis}}" />
            <ComboBox Grid.Column="2" Margin="8,0,0,0" MinWidth="150" VerticalAlignment="Center"
                      ItemsSource="{Binding CategoryFilterOptions}"
                      SelectedItem="{Binding SelectedCategoryFilter}"
                      DisplayMemberPath="Name" />
            <ui:Button Grid.Column="3" Margin="8,0,0,0" Appearance="Secondary"
                       Content="Categories…" Command="{Binding ManageCategoriesCommand}"
                       ToolTip="Add, rename, or delete categories" />
        </Grid>
```

Then change:

```xml
        <!-- Phrases exist, but the current search/filter hides them all (distinct from first-run) -->
        <ui:Card VerticalAlignment="Center" HorizontalAlignment="Center" Padding="24" MaxWidth="320"
                 Visibility="{Binding NoMatches, Converter={StaticResource BoolToVis}}">
            <StackPanel>
                <TextBlock Text="No phrases match" HorizontalAlignment="Center"
                           FontSize="{StaticResource FontSize.SectionTitle}" FontWeight="SemiBold" />
                <TextBlock Text="Try a different search or category."
                           Foreground="{StaticResource Text.Secondary}" TextWrapping="Wrap"
                           TextAlignment="Center" Margin="0,8,0,0" />
            </StackPanel>
        </ui:Card>
```

to:

```xml
        <!-- A search is active and matches nothing (distinct from first-run and from
             CategoryIsEmpty above). SearchText has a public setter, so Mode=OneWay isn't strictly
             required here for correctness — added anyway for consistency with the Global
             Constraints rule and to make every new Run in this file follow the same rule. -->
        <ui:Card VerticalAlignment="Center" HorizontalAlignment="Center" Padding="24" MaxWidth="320"
                 Visibility="{Binding SearchNoMatch, Converter={StaticResource BoolToVis}}">
            <StackPanel>
                <TextBlock HorizontalAlignment="Center"
                           FontSize="{StaticResource FontSize.SectionTitle}" FontWeight="SemiBold">
                    <Run Text="No phrases match '" /><Run Text="{Binding SearchText, Mode=OneWay}" /><Run Text="'" />
                </TextBlock>
                <ui:Button Content="Clear search" Appearance="Secondary" HorizontalAlignment="Center"
                           Command="{Binding ClearSearchCommand}" Margin="0,12,0,0" />
            </StackPanel>
        </ui:Card>
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "Search_filters_by_title_case_insensitively|No_matches_state_is_distinct_from_first_run_empty|Search_no_match_is_true_even_with_a_category_selected|Has_search_text_reflects_whether_a_query_is_active|Clear_search_resets_the_search_text"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Run the full App suite and build to verify no regressions**

Run: `dotnet build src/AdaVoice.App --nologo && dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` and all tests pass (165 prior + 3 net-new
[`Search_no_match_is_true_even_with_a_category_selected`, `Has_search_text_reflects_whether_a_query_is_active`,
`Clear_search_resets_the_search_text`] = 168 — the other two tests changed in place, not added).

- [ ] **Step 7: Commit**

```bash
git add src/AdaVoice.App/ViewModels/BoardViewModel.cs src/AdaVoice.App/MainWindow.xaml tests/AdaVoice.App.Tests/BoardViewModelTests.cs
git commit -m "feat(app): search Clear button and query echo on the no-match card"
```

---

### Task 4: Repair dialog for broken phrases

**Files:**
- Create: `src/AdaVoice.App/ViewModels/RepairPhraseViewModel.cs`
- Create: `tests/AdaVoice.App.Tests/RepairPhraseViewModelTests.cs`
- Create: `src/AdaVoice.App/RepairPhraseDialog.xaml` + `.xaml.cs`
- Modify: `src/AdaVoice.App/ViewModels/BoardViewModel.cs`
- Modify: `src/AdaVoice.App/MainWindow.xaml.cs`
- Modify: `src/AdaVoice.App/App.xaml.cs`
- Modify: `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`

This task is intentionally one unit of work, not split further: `BoardViewModel`'s new
`_showRepairDialog` parameter, the `MainWindow` method that shows the dialog, and
`App.xaml.cs`'s wiring are mutually dependent — splitting them would leave the solution unbuildable
between commits, which the settings-window plan hit the same constraint on (its Task 7).

**Interfaces:**
- Consumes: `_pendingMetadata` (Task 2, extended here to also carry `Tags`), `StartRecording()`
  (Task 1), `DeleteEntry` (existing `ILibraryHost` member).
- Produces: `RepairPhraseViewModel(PhraseEntry)` with `Title`, `Choice`, `ChooseReRecord()`,
  `ChooseRemove()`; `RepairChoice` enum; `MainWindow.ShowRepairDialog(RepairPhraseViewModel):
  bool`. This is the last task in this plan.

- [ ] **Step 1: Write the failing `RepairPhraseViewModel` tests**

Create `tests/AdaVoice.App.Tests/RepairPhraseViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class RepairPhraseViewModelTests
{
    [Fact]
    public void Exposes_the_entrys_title()
    {
        var vm = new RepairPhraseViewModel(new PhraseEntry { Id = "p-1", Title = "Hi" });

        Assert.Equal("Hi", vm.Title);
    }

    [Fact]
    public void Starts_with_no_choice_made()
    {
        var vm = new RepairPhraseViewModel(new PhraseEntry { Id = "p-1" });

        Assert.Null(vm.Choice);
    }

    [Fact]
    public void Choose_re_record_records_the_choice()
    {
        var vm = new RepairPhraseViewModel(new PhraseEntry { Id = "p-1" });

        vm.ChooseReRecord();

        Assert.Equal(RepairChoice.ReRecord, vm.Choice);
    }

    [Fact]
    public void Choose_remove_records_the_choice()
    {
        var vm = new RepairPhraseViewModel(new PhraseEntry { Id = "p-1" });

        vm.ChooseRemove();

        Assert.Equal(RepairChoice.Remove, vm.Choice);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter RepairPhraseViewModelTests`
Expected: FAIL — `RepairPhraseViewModel` does not exist.

- [ ] **Step 3: Implement `RepairPhraseViewModel`**

Create `src/AdaVoice.App/ViewModels/RepairPhraseViewModel.cs`:

```csharp
using AdaVoice.Core.Domain;

namespace AdaVoice.App.ViewModels;

/// <summary>What the operator chose in the repair-phrase dialog.</summary>
public enum RepairChoice
{
    ReRecord,
    Remove,
}

/// <summary>Backs the repair-phrase dialog for a broken (audio-missing) phrase. Plain state and
/// two setters the dialog's buttons call directly — no commands needed, since the dialog's
/// code-behind records the choice and closes with <c>DialogResult = true</c> in the same click
/// handler (mirrors <see cref="PhraseEditViewModel"/>'s "caller reads state after ShowDialog"
/// shape, simplified since there's no form data to edit here).</summary>
public sealed class RepairPhraseViewModel(PhraseEntry entry)
{
    /// <summary>The broken phrase's title, shown in the dialog.</summary>
    public string Title => entry.Title;

    /// <summary>What the operator chose, or null if the dialog is still open / was cancelled.</summary>
    public RepairChoice? Choice { get; private set; }

    public void ChooseReRecord() => Choice = RepairChoice.ReRecord;
    public void ChooseRemove() => Choice = RepairChoice.Remove;
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter RepairPhraseViewModelTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Write the failing `BoardViewModel` tests**

In `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`, first update the `NewBoard` helper to accept
the new delegate:

```csharp
    private static BoardViewModel NewBoard(
        FakePlaybackHost host,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<SetupWizardViewModel>? showSetupWizard = null,
        ISettingsHost? settingsHost = null,
        Action<SettingsWindowViewModel>? showSettings = null) =>
        new(host, host, host, host, settingsHost ?? new FakeSettingsHost(), new StatusViewModel(host),
            new SettingsViewModel(new FakeSettingsHost()),
            getActiveHotkey: () => "Pause", confirmDelete: confirmDelete, showEditDialog: showEditDialog,
            showManageCategories: showManageCategories, showSetupWizard: showSetupWizard,
            showSettings: showSettings);
```

to:

```csharp
    private static BoardViewModel NewBoard(
        FakePlaybackHost host,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<SetupWizardViewModel>? showSetupWizard = null,
        ISettingsHost? settingsHost = null,
        Action<SettingsWindowViewModel>? showSettings = null,
        Func<RepairPhraseViewModel, bool>? showRepairDialog = null) =>
        new(host, host, host, host, settingsHost ?? new FakeSettingsHost(), new StatusViewModel(host),
            new SettingsViewModel(new FakeSettingsHost()),
            getActiveHotkey: () => "Pause", confirmDelete: confirmDelete, showEditDialog: showEditDialog,
            showManageCategories: showManageCategories, showSetupWizard: showSetupWizard,
            showSettings: showSettings, showRepairDialog: showRepairDialog);
```

Then replace the existing broken-phrase test. Change:

```csharp
    [Fact]
    public void Play_a_broken_phrase_shows_a_notice_and_does_not_play()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
            BrokenPhraseIds = ["p-1"],
        };
        var board = NewBoard(host);

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.DoesNotContain("PlayEntry", host.Calls);
        Assert.NotNull(board.Notice);
    }
```

to:

```csharp
    [Fact]
    public void Play_a_broken_phrase_opens_the_repair_dialog_instead_of_a_notice()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", FileName = "p-1.wav" }],
            BrokenPhraseIds = ["p-1"],
        };
        RepairPhraseViewModel? shown = null;
        var board = NewBoard(host, showRepairDialog: repair => { shown = repair; return false; }); // cancelled

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.DoesNotContain("PlayEntry", host.Calls);
        Assert.NotNull(shown);
        Assert.Equal("Hi", shown!.Title);
        Assert.Single(board.Phrases); // cancelled — nothing removed
    }

    [Fact]
    public void Repair_dialog_remove_deletes_the_broken_phrase()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi" }],
            BrokenPhraseIds = ["p-1"],
        };
        string? deleted = null;
        var board = NewBoard(host, showRepairDialog: repair => { repair.ChooseRemove(); return true; });
        board.Deleted += (_, title) => deleted = title;

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.Empty(board.Phrases);
        Assert.Equal("Hi", deleted);
    }

    [Fact]
    public async Task Repair_dialog_re_record_removes_the_old_entry_and_starts_recording_prefilled()
    {
        var host = new FakePlaybackHost
        {
            CanRecord = true,
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-2", Tags = ["urgent"] }],
            BrokenPhraseIds = ["p-1"],
        };
        var board = NewBoard(host, showRepairDialog: repair => { repair.ChooseReRecord(); return true; });

        await board.PlayCommand.ExecuteAsync(board.Phrases[0]);

        Assert.Empty(board.Phrases); // old broken entry removed
        Assert.Equal("Hi", board.NewTitle); // pre-filled from the broken entry
        Assert.True(board.IsRecording);

        board.PendingTake = Take();
        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Hi");
        Assert.Equal("c-2", saved.CategoryId);
        Assert.Equal(["urgent"], saved.Tags);
    }
```

- [ ] **Step 6: Run to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "Play_a_broken_phrase_opens_the_repair_dialog_instead_of_a_notice|Repair_dialog_remove_deletes_the_broken_phrase|Repair_dialog_re_record_removes_the_old_entry_and_starts_recording_prefilled"`
Expected: FAIL to compile — `_showRepairDialog`/the new `NewBoard` parameter don't exist on
`BoardViewModel` yet.

- [ ] **Step 7: Wire `BoardViewModel`**

In `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, add the field next to the other delegate
fields (near `_showEditDialog`):

```csharp
    private readonly Func<RepairPhraseViewModel, bool> _showRepairDialog;
```

Add the constructor parameter (after `showEditDialog`) and its default assignment (after
`_showEditDialog`'s assignment):

```csharp
        Func<RepairPhraseViewModel, bool>? showRepairDialog = null,
```

```csharp
        _showRepairDialog = showRepairDialog ?? (_ => false); // default: cancel (unit tests opt in)
```

Then change `Play` from:

```csharp
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
```

to:

```csharp
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
                    NewTitle = item.Entry.Title;
                    _pendingMetadata = (item.Entry.CategoryId, item.Entry.Tags);
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
            Notice = "Start the engine (and be ON AIR) to play to the call.";
            return;
        }

        Notice = null;
        _playback.PlayEntry(item.Entry);
    }
```

- [ ] **Step 8: Create the dialog view**

Create `src/AdaVoice.App/RepairPhraseDialog.xaml`:

```xml
<Window x:Class="AdaVoice.App.RepairPhraseDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        Title="Repair phrase"
        Width="360" SizeToContent="Height"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False"
        Background="{StaticResource Surface.Window}"
        TextElement.Foreground="{StaticResource Text.Primary}"
        FontFamily="Segoe UI Variable, Segoe UI" FontSize="14">
    <StackPanel Margin="16">
        <TextBlock Text="{Binding Title}" FontWeight="SemiBold"
                   FontSize="{StaticResource FontSize.SectionTitle}" TextWrapping="Wrap" />
        <TextBlock Text="⚠ This phrase's audio file is missing." Foreground="{StaticResource Text.Secondary}"
                   TextWrapping="Wrap" Margin="0,8,0,0" />

        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <ui:Button Content="Cancel" Appearance="Secondary" IsCancel="True" Margin="0,0,8,0" />
            <ui:Button Content="Remove" Appearance="Secondary" Margin="0,0,8,0" Click="Remove_Click" />
            <ui:Button Content="Re-record" Appearance="Primary" IsDefault="True" Click="ReRecord_Click" />
        </StackPanel>
    </StackPanel>
</Window>
```

Create `src/AdaVoice.App/RepairPhraseDialog.xaml.cs`:

```csharp
using System.Windows;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

/// <summary>Modal "repair phrase" prompt for a broken (audio-missing) phrase. Its
/// <c>DataContext</c> is a <c>RepairPhraseViewModel</c>; the caller reads
/// <see cref="RepairPhraseViewModel.Choice"/> after <see cref="Window.ShowDialog"/> returns true.</summary>
public partial class RepairPhraseDialog : Window
{
    public RepairPhraseDialog() => InitializeComponent();

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        ((RepairPhraseViewModel)DataContext).ChooseRemove();
        DialogResult = true;
    }

    private void ReRecord_Click(object sender, RoutedEventArgs e)
    {
        ((RepairPhraseViewModel)DataContext).ChooseReRecord();
        DialogResult = true;
    }
}
```

- [ ] **Step 9: Wire `MainWindow` and `App.xaml.cs`**

In `src/AdaVoice.App/MainWindow.xaml.cs`, add next to `ShowEditDialog`:

```csharp
    /// <summary>Show the modal repair-phrase prompt; returns true if the operator chose an action
    /// (Re-record or Remove), false if they cancelled.</summary>
    public bool ShowRepairDialog(RepairPhraseViewModel repair) =>
        new RepairPhraseDialog { DataContext = repair, Owner = this }.ShowDialog() == true;
```

In `src/AdaVoice.App/App.xaml.cs`, change:

```csharp
        var board = new BoardViewModel(
            _host, _host, _host, _host, _host, status, settings,
            () => window.ActiveHotkey,
            action => Dispatcher.BeginInvoke(action),
            confirmDelete: window.ConfirmDelete,
            showEditDialog: window.ShowEditDialog,
            showManageCategories: window.ShowManageCategories,
            showSetupWizard: window.ShowSetupWizard,
            showSettings: window.ShowSettings,
            pickExportPath: window.PickExportPath,
            pickImportFile: window.PickImportFile,
            confirmAndRestart: window.ConfirmAndRestart,
            showError: window.ShowError,
            showSettingsInfo: window.ShowInfo);
```

to:

```csharp
        var board = new BoardViewModel(
            _host, _host, _host, _host, _host, status, settings,
            () => window.ActiveHotkey,
            action => Dispatcher.BeginInvoke(action),
            confirmDelete: window.ConfirmDelete,
            showEditDialog: window.ShowEditDialog,
            showManageCategories: window.ShowManageCategories,
            showSetupWizard: window.ShowSetupWizard,
            showSettings: window.ShowSettings,
            pickExportPath: window.PickExportPath,
            pickImportFile: window.PickImportFile,
            confirmAndRestart: window.ConfirmAndRestart,
            showError: window.ShowError,
            showSettingsInfo: window.ShowInfo,
            showRepairDialog: window.ShowRepairDialog);
```

If Tasks 1-3 changed this call's exact formatting (they should not have — none of them add a
constructor parameter), match the new argument in by name, not position.

- [ ] **Step 10: Run the new tests to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo --filter "Play_a_broken_phrase_opens_the_repair_dialog_instead_of_a_notice|Repair_dialog_remove_deletes_the_broken_phrase|Repair_dialog_re_record_removes_the_old_entry_and_starts_recording_prefilled|RepairPhraseViewModelTests"`
Expected: PASS, 7 tests (3 `BoardViewModel` + 4 `RepairPhraseViewModel`).

- [ ] **Step 11: Run the full App suite and build to verify no regressions**

Run: `dotnet build src/AdaVoice.App --nologo && dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).` and all tests pass (168 prior − 1 replaced +
3 new BoardViewModel tests + 4 new RepairPhraseViewModel tests = 174).

- [ ] **Step 12: Commit**

```bash
git add src/AdaVoice.App/ViewModels/RepairPhraseViewModel.cs tests/AdaVoice.App.Tests/RepairPhraseViewModelTests.cs src/AdaVoice.App/RepairPhraseDialog.xaml src/AdaVoice.App/RepairPhraseDialog.xaml.cs src/AdaVoice.App/ViewModels/BoardViewModel.cs src/AdaVoice.App/MainWindow.xaml.cs src/AdaVoice.App/App.xaml.cs tests/AdaVoice.App.Tests/BoardViewModelTests.cs
git commit -m "feat(app): repair dialog for broken phrases (re-record / remove)"
```

---

### Task 5: Wizard per-row spinner (cosmetic)

**Files:**
- Modify: `src/AdaVoice.App/EnvironmentChecksStepView.xaml`
- Modify: `src/AdaVoice.App/EnvironmentChecksStepView.xaml.cs`

No test file — this is purely cosmetic, View-owned, and verified only by manual smoke test, the
same treatment the calibration countdown ring got. No `EnvironmentChecksStepViewModel` change: the
checks run and complete instantly (`EnvironmentChecks.Run` has nothing to await), so there is
nothing real to report progress on — this is a deliberate, brief "it's working" pause before the
results appear, matching design 05 §2's "spinner → ✓/✗" language.

**Simplification from the spec:** the spec describes a per-row *staggered* reveal (each row's
spinner clearing slightly after the previous one). A true per-row stagger needs either a bindable
`Storyboard.BeginTime` (not supported declaratively per-item in WPF without real complexity) or
code-behind that walks `ItemContainerGenerator` containers. Since the whole thing is decorative —
the checks are not real async work — this task uses one shared spinner-then-reveal-all transition
instead: simpler, no new converter, and it still satisfies design 05's intent (spinner, then
result) even though all four rows reveal at the same moment rather than staggered.

**Interfaces:**
- Consumes: `EnvironmentChecksStepViewModel.Checks` (existing, unchanged), which raises
  `PropertyChanged` on every `Recheck` (it's an `[ObservableProperty]`).
- Produces: nothing consumed by other tasks — this is the last task in the plan.

- [ ] **Step 1: Add the spinner/list swap to the XAML**

In `src/AdaVoice.App/EnvironmentChecksStepView.xaml`, change:

```xml
    <StackPanel>
        <TextBlock Text="Environment checks" FontWeight="SemiBold"
                   FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />
        <ItemsControl ItemsSource="{Binding Checks}">
```

to:

```xml
    <StackPanel>
        <TextBlock Text="Environment checks" FontWeight="SemiBold"
                   FontSize="{StaticResource FontSize.SectionTitle}" Margin="0,0,0,8" />

        <!-- Brief spinner shown before results appear. The checks actually run instantly — this is
             a deliberate cosmetic pause (design 05 §2: "each row: spinner → ✓/✗").
             Swapped for ChecksList by the timer in EnvironmentChecksStepView.xaml.cs. -->
        <StackPanel x:Name="SpinnerPanel" Orientation="Horizontal" Margin="0,0,0,8">
            <ProgressBar IsIndeterminate="True" Width="16" Height="16" />
            <TextBlock Text="Checking…" Margin="8,0,0,0" VerticalAlignment="Center"
                       Foreground="{StaticResource Text.Secondary}" />
        </StackPanel>

        <ItemsControl x:Name="ChecksList" Visibility="Collapsed" ItemsSource="{Binding Checks}">
```

Then find the matching `</ItemsControl>` closing tag later in the same file and leave it as-is (no
change needed there — only the opening tag gained `x:Name`/`Visibility`).

- [ ] **Step 2: Add the reveal timer to the code-behind**

In `src/AdaVoice.App/EnvironmentChecksStepView.xaml.cs`, change:

```csharp
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace AdaVoice.App;

/// <summary>The environment-checks step's view. Its <c>DataContext</c> is an
/// <c>EnvironmentChecksStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class EnvironmentChecksStepView : UserControl
{
    public EnvironmentChecksStepView() => InitializeComponent();

    /// <summary>Opens the VB-CABLE download link in the operator's default browser. A pure OS
    /// action with nothing to unit-test, so it lives here rather than in the ViewModel or a new
    /// host seam — there is no business logic to isolate, just a single link click.</summary>
    private void OnVbCableLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
```

to:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Windows.Threading;
using AdaVoice.App.ViewModels;

namespace AdaVoice.App;

/// <summary>The environment-checks step's view. Its <c>DataContext</c> is an
/// <c>EnvironmentChecksStepViewModel</c>, set by the wizard window's DataTemplate.</summary>
public partial class EnvironmentChecksStepView : UserControl
{
    // Purely cosmetic — the checks themselves run instantly, so this just gives the operator a
    // brief "it's working" moment before the results appear (design 05 §2).
    private readonly DispatcherTimer _revealTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public EnvironmentChecksStepView()
    {
        InitializeComponent();
        _revealTimer.Tick += (_, _) => Reveal();
        Loaded += (_, _) => RestartReveal();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is EnvironmentChecksStepViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is EnvironmentChecksStepViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-check pressed (Checks was reassigned) — show the spinner again before the new results.
        if (e.PropertyName == nameof(EnvironmentChecksStepViewModel.Checks))
            RestartReveal();
    }

    private void RestartReveal()
    {
        _revealTimer.Stop();
        SpinnerPanel.Visibility = Visibility.Visible;
        ChecksList.Visibility = Visibility.Collapsed;
        _revealTimer.Start();
    }

    private void Reveal()
    {
        _revealTimer.Stop();
        SpinnerPanel.Visibility = Visibility.Collapsed;
        ChecksList.Visibility = Visibility.Visible;
    }

    /// <summary>Opens the VB-CABLE download link in the operator's default browser. A pure OS
    /// action with nothing to unit-test, so it lives here rather than in the ViewModel or a new
    /// host seam — there is no business logic to isolate, just a single link click.</summary>
    private void OnVbCableLinkRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/AdaVoice.App --nologo`
Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 4: Run the full App suite to verify no regressions**

Run: `dotnet test tests/AdaVoice.App.Tests --nologo`
Expected: PASS, all 174 tests (no new tests this task — cosmetic, View-owned, per the Files note
above).

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.App/EnvironmentChecksStepView.xaml src/AdaVoice.App/EnvironmentChecksStepView.xaml.cs
git commit -m "feat(app): cosmetic per-check spinner before the wizard's environment-check results"
```

---

## After all 5 tasks: manual smoke test

Add to the existing Settings-window smoke-test checklist (or run standalone) before calling this
slice done:

1. Category filter → pick an empty category → see "No phrases in {category} yet." + the Record
   button → record a phrase → it lands in that category.
2. Search for a term that matches nothing → see the query echoed in quotes + a Clear search button
   → click it → search clears, board returns to normal.
3. Type then clear the search box manually (✕ button next to it) → same result.
4. Click a broken phrase (if none exist, temporarily rename/move its WAV file to force
   `BrokenPhraseIds` to include it) → repair dialog opens → try Cancel, then Remove, then
   Re-record (re-record a phrase and confirm it keeps the original's category/tags).
5. Record a phrase → watch for "Processing…" appearing briefly after clicking Stop, before the
   Save/Discard bar appears (no flash of the idle Record button in between).
6. Try to Save a take with a blank title → Save button is disabled.
7. Run Setup → environment checks step → confirm a brief "Checking…" spinner appears before the
   four check rows show their results; click Re-check → spinner reappears.
