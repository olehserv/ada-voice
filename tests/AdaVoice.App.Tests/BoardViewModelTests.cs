using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Core.Domain;
using AdaVoice.Host;

namespace AdaVoice.App.Tests;

public class BoardViewModelTests
{
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

    private static RecordingResult Take() => new(new float[10], GainDb: -3, DurationMs: 1000, PeakDbfs: -6);

    [Fact]
    public void Phrases_come_from_the_host()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] };

        Assert.Single(NewBoard(host).Phrases);
    }

    [Fact]
    public void Play_command_plays_that_phrase_to_the_call_when_live()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
        };
        var board = NewBoard(host);
        var item = board.Phrases[0];

        board.PlayCommand.Execute(item);

        Assert.Equal("PlayEntry", Assert.Single(host.Calls));
        Assert.Same(item.Entry, host.PlayedEntry);
    }

    [Fact]
    public void Play_when_not_live_shows_a_notice_and_does_not_play()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Stopped,
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
        };
        var board = NewBoard(host);

        board.PlayCommand.Execute(board.Phrases[0]);

        Assert.DoesNotContain("PlayEntry", host.Calls);
        Assert.NotNull(board.Notice);
    }

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
    public void Repair_dialog_re_record_removes_the_old_entry_and_starts_recording_prefilled()
    {
        var host = new FakePlaybackHost
        {
            CanRecord = true,
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-2", Tags = ["urgent"] }],
            BrokenPhraseIds = ["p-1"],
        };
        var board = NewBoard(host, showRepairDialog: repair => { repair.ChooseReRecord(); return true; });

        // Block instead of `await`: awaiting here would resume this test method's continuation on
        // whatever threadpool thread completed StartRecording's Task.Run (no SynchronizationContext
        // in this headless xunit host) — and the SaveTake below touches the Phrases ObservableCollection,
        // which WPF's CollectionView ties to the thread that first created it (here, this test's own
        // thread). GetAwaiter().GetResult() waits for the same full completion but keeps running on
        // this thread, avoiding the "different thread" CollectionView exception (same hazard the
        // Save/RecordIntoCategory tests above avoid by using .Execute() instead of awaiting). No
        // deadlock risk: nothing downstream marshals back to this (blocked) thread.
#pragma warning disable xUnit1031
        board.PlayCommand.ExecuteAsync(board.Phrases[0]).GetAwaiter().GetResult();
#pragma warning restore xUnit1031

        Assert.Empty(board.Phrases); // old broken entry removed
        Assert.Equal("Hi", board.NewTitle); // pre-filled from the broken entry
        Assert.True(board.IsRecording);

        board.PendingTake = Take();
        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Hi");
        Assert.Equal("c-2", saved.CategoryId);
        Assert.Equal(["urgent"], saved.Tags);
    }

    [Fact]
    public async Task Test_on_headphones_previews_the_entry_off_the_call()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Stopped, // works even with the engine stopped
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
        };
        var board = NewBoard(host);
        var item = board.Phrases[0];

        await board.TestOnHeadphonesCommand.ExecuteAsync(item);

        Assert.Contains("PreviewEntry", host.Calls);
        Assert.Same(item.Entry, host.PreviewedEntry);
        Assert.DoesNotContain("PlayEntry", host.Calls); // never toward the call
    }

    [Fact]
    public async Task Test_on_headphones_surfaces_a_preview_error_as_a_notice()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }],
            PreviewEntryResult = "missing audio file: p-1.wav",
        };
        var board = NewBoard(host);

        await board.TestOnHeadphonesCommand.ExecuteAsync(board.Phrases[0]);

        Assert.Equal("missing audio file: p-1.wav", board.Notice);
    }

    [Fact]
    public void Playing_phrase_changed_highlights_only_that_item()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1" }, new PhraseEntry { Id = "p-2" }],
        };
        var board = NewBoard(host);

        host.RaisePlayingPhraseChanged("p-2");

        Assert.False(board.Phrases[0].IsPlaying);
        Assert.True(board.Phrases[1].IsPlaying);
    }

    [Fact]
    public void Playing_phrase_null_clears_every_highlight()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] };
        var board = NewBoard(host);
        host.RaisePlayingPhraseChanged("p-1");

        host.RaisePlayingPhraseChanged(null);

        Assert.All(board.Phrases, i => Assert.False(i.IsPlaying));
    }

    [Fact]
    public void Stop_command_stops_the_current_phrase()
    {
        var host = new FakePlaybackHost();

        NewBoard(host).StopCommand.Execute(null);

        Assert.Equal("StopPhrase", Assert.Single(host.Calls));
    }

    [Fact]
    public void Start_and_stop_engine_commands_drive_the_host()
    {
        var host = new FakePlaybackHost();
        var board = NewBoard(host);

        board.StartEngineCommand.Execute(null);
        board.StopEngineCommand.Execute(null);

        Assert.Equal(["Start", "Stop"], host.Calls);
    }

    [Fact]
    public void Off_air_toggle_enters_then_exits()
    {
        var host = new FakePlaybackHost { State = EngineState.Live };
        var board = NewBoard(host);

        board.ToggleOffAirCommand.Execute(null); // Live -> enter OFF AIR
        host.State = EngineState.OffAir;
        board.ToggleOffAirCommand.Execute(null); // OFF AIR -> exit

        Assert.Equal(["EnterOffAir", "ExitOffAir"], host.Calls);
    }

    [Fact]
    public async Task Start_recording_enters_recording_when_the_host_allows_it()
    {
        var board = NewBoard(new FakePlaybackHost { CanRecord = true });

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.True(board.IsRecording);
    }

    [Fact]
    public async Task Start_recording_shows_a_notice_when_not_live()
    {
        var board = NewBoard(new FakePlaybackHost { CanRecord = false });

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.False(board.IsRecording);
        Assert.NotNull(board.Notice);
    }

    // The host can throw (e.g. the mic vanished between OFF AIR and opening the capture) — the
    // command must surface a notice, never crash the app.
    [Fact]
    public async Task Start_recording_failure_becomes_a_notice()
    {
        var board = NewBoard(new FakePlaybackHost { TryStartRecordingThrows = true });

        await board.StartRecordingCommand.ExecuteAsync(null);

        Assert.False(board.IsRecording);
        Assert.NotNull(board.Notice);
    }

    [Fact]
    public async Task Stop_recording_with_signal_holds_a_pending_take()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);

        await board.StopRecordingCommand.ExecuteAsync(null);

        Assert.True(board.HasPendingTake);
        Assert.False(board.IsRecording);
    }

    [Fact]
    public async Task Stop_recording_with_no_signal_keeps_nothing_and_notices()
    {
        var host = new FakePlaybackHost { NextStopResult = RecordingResult.NoSignal };
        var board = NewBoard(host);

        await board.StopRecordingCommand.ExecuteAsync(null);

        Assert.False(board.HasPendingTake);
        Assert.NotNull(board.Notice);
    }

    // Save tests arrange the pending take directly: awaiting the async stop command would hop
    // the test off the thread that owns the Phrases CollectionView (a WPF test artifact — in
    // the app the await resumes on the dispatcher). The stop flow has its own tests above.
    [Fact]
    public void Save_take_saves_with_the_title_and_refreshes_the_board()
    {
        var host = new FakePlaybackHost();
        var board = NewBoard(host);
        board.PendingTake = Take();
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        Assert.Contains("SaveTake", host.Calls);
        Assert.Equal("Greeting", host.SavedTitle);
        Assert.False(board.HasPendingTake);
        Assert.Contains(board.Phrases, i => i.Title == "Greeting"); // appears on the board
    }

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

    // The recorder save (WAV write + entry creation) already succeeded before the category/tag
    // step fails — retrying Save would call _recorder.SaveTake again and create a duplicate entry.
    // The take must be treated as saved; only a warning notice tells the operator to fix it manually.
    [Fact]
    public void Save_take_with_a_pending_category_that_fails_to_apply_still_completes_the_save()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-2", Name = "Closers" }],
            SetPhraseCategoryThrows = true,
        };
        var board = NewBoard(host);
        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");
        board.RecordIntoCategoryCommand.Execute(null); // arranges _pendingMetadata.CategoryId synchronously (before its await)
        board.PendingTake = Take();
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        Assert.False(board.HasPendingTake); // the take IS considered saved, not lost
        Assert.Contains(board.Phrases, p => p.Title == "Greeting"); // it's on the board
        Assert.NotNull(board.Notice); // but the operator is warned
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

    [Fact]
    public void IsEmpty_reflects_whether_the_board_has_phrases()
    {
        Assert.True(NewBoard(new FakePlaybackHost()).IsEmpty);
        Assert.False(NewBoard(new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] }).IsEmpty);
    }

    [Fact]
    public void Saving_the_first_take_clears_the_empty_state_and_notifies()
    {
        var host = new FakePlaybackHost();
        var board = NewBoard(host);
        board.PendingTake = Take();
        Assert.True(board.IsEmpty);

        var changed = new List<string?>();
        board.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        board.SaveTakeCommand.Execute(null);

        Assert.False(board.IsEmpty);
        Assert.Contains(nameof(BoardViewModel.IsEmpty), changed);
    }

    [Fact]
    public void Saving_a_take_raises_the_Saved_event_with_the_title()
    {
        var host = new FakePlaybackHost();
        var board = NewBoard(host);
        board.PendingTake = Take();
        board.NewTitle = "Hello";

        string? saved = null;
        board.Saved += (_, title) => saved = title;

        board.SaveTakeCommand.Execute(null);

        Assert.Equal("Hello", saved);
    }

    [Fact]
    public async Task Discard_take_clears_it_without_saving()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);
        await board.StopRecordingCommand.ExecuteAsync(null);

        board.DiscardTakeCommand.Execute(null);

        Assert.False(board.HasPendingTake);
        Assert.DoesNotContain("SaveTake", host.Calls);
    }

    [Fact]
    public async Task Preview_take_plays_the_pending_samples_to_the_monitor()
    {
        var take = Take();
        var host = new FakePlaybackHost { NextStopResult = take };
        var board = NewBoard(host);
        await board.StopRecordingCommand.ExecuteAsync(null);

        await board.PreviewTakeCommand.ExecuteAsync(null);

        Assert.Contains("Preview", host.Calls);
        Assert.Same(take.Samples, host.PreviewedSamples);
    }

    // ---- Edit / delete -----------------------------------------------------------------------

    [Fact]
    public void Broken_phrases_are_flagged_from_the_host()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1" }, new PhraseEntry { Id = "p-2" }],
            BrokenPhraseIds = ["p-2"],
        };
        var board = NewBoard(host);

        Assert.False(board.Phrases[0].IsBroken);
        Assert.True(board.Phrases[1].IsBroken);
    }

    [Fact]
    public void Edit_command_updates_the_item_in_place_when_the_dialog_commits()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Old", CategoryId = Category.DefaultId }],
        };
        // The dialog edits the view-model, then "commits" (returns true).
        var board = NewBoard(host, showEditDialog: edit => { edit.Title = "New"; return true; });
        var item = board.Phrases[0];

        board.EditCommand.Execute(item);

        Assert.Equal("New", item.Title);              // same item, refreshed (the immutability trap)
        Assert.Equal("New", host.Phrases[0].Title);   // persisted through the seam
    }

    [Fact]
    public void Editing_a_phrase_out_of_the_active_filter_hides_it_from_the_view()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        var board = NewBoard(host, showEditDialog: edit =>
        {
            edit.SelectedCategoryId = Category.DefaultId; // move it out of "Greetings"
            return true;
        });
        board.SelectedCategoryFilter = board.CategoryFilterOptions.First(c => c.Id == "c-1");
        Assert.Single(VisibleTitles(board));

        board.EditCommand.Execute(board.Phrases[0]);

        Assert.Empty(VisibleTitles(board)); // moved out of the filtered view
        // The category is now genuinely empty (not just search-filtered) — CategoryIsEmpty owns
        // this case; SearchNoMatch is deliberately false here (it requires an active search).
        Assert.True(board.CategoryIsEmpty);
        Assert.False(board.SearchNoMatch);
    }

    [Fact]
    public void Edit_command_cancelled_changes_nothing()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Old", CategoryId = Category.DefaultId }],
        };
        var board = NewBoard(host, showEditDialog: edit => { edit.Title = "New"; return false; });
        var item = board.Phrases[0];

        board.EditCommand.Execute(item);

        Assert.Equal("Old", item.Title);
        Assert.Equal("Old", host.Phrases[0].Title);
    }

    [Fact]
    public void Delete_command_orphans_removes_and_raises_Deleted_when_confirmed()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1", Title = "Bye" }] };
        var board = NewBoard(host, confirmDelete: _ => true);
        var item = board.Phrases[0];
        string? deleted = null;
        board.Deleted += (_, title) => deleted = title;

        board.DeleteCommand.Execute(item);

        Assert.Empty(board.Phrases);
        Assert.Equal("p-1", Assert.Single(host.Deleted).Id);
        Assert.Equal("Bye", deleted);
    }

    [Fact]
    public void Delete_command_does_nothing_when_not_confirmed()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] };
        var board = NewBoard(host, confirmDelete: _ => false);

        board.DeleteCommand.Execute(board.Phrases[0]);

        Assert.Single(board.Phrases);
        Assert.Empty(host.Deleted);
    }

    // ---- Category colour ---------------------------------------------------------------------

    [Fact]
    public void Phrase_items_carry_their_category_colour()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings", Color = "#54D262" }],
            Phrases = [new PhraseEntry { Id = "p-1", CategoryId = "c-1" }],
        };

        var board = NewBoard(host);

        Assert.Equal("#54D262", board.Phrases[0].CategoryColor);
    }

    [Fact]
    public void Editing_a_phrase_into_another_category_updates_its_colour()
    {
        var host = new FakePlaybackHost
        {
            Categories =
            [
                new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" },
                new Category { Id = "c-1", Name = "Greetings", Color = "#54D262" },
            ],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = Category.DefaultId }],
        };
        var board = NewBoard(host, showEditDialog: edit => { edit.SelectedCategoryId = "c-1"; return true; });

        board.EditCommand.Execute(board.Phrases[0]);

        Assert.Equal("#54D262", board.Phrases[0].CategoryColor);
    }

    [Fact]
    public void A_saved_take_gets_the_default_category_colour()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" }],
        };
        var board = NewBoard(host);
        board.PendingTake = Take();

        board.SaveTakeCommand.Execute(null);

        Assert.Equal("#808080", board.Phrases[0].CategoryColor);
    }

    [Fact]
    public void Recolouring_a_category_in_the_manager_re_tints_its_tiles()
    {
        var host = new FakePlaybackHost
        {
            Categories =
            [
                new Category { Id = Category.DefaultId, Name = "Uncategorized", Color = "#808080" },
                new Category { Id = "c-1", Name = "Greetings", Color = "#54D262" },
            ],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = "c-1" }],
        };
        // The "manager" recolours Greetings and saves, like the user would.
        var board = NewBoard(host, showManageCategories: vm =>
        {
            var row = vm.Rows.First(r => r.Id == "c-1");
            row.Color = "#FF6B6B"; // the dropdown sets the row's colour
            vm.SaveCommand.Execute(row);
        });

        board.ManageCategoriesCommand.Execute(null);

        Assert.Equal("#FF6B6B", board.Phrases[0].CategoryColor);
    }

    // ---- Tag chips -----------------------------------------------------------------------------

    [Fact]
    public void Phrase_items_carry_their_tags_as_coloured_chips()
    {
        var host = new FakePlaybackHost
        {
            Tags = [new TagInfo { Name = "opening", Color = "#4CC2FF" }, new TagInfo { Name = "urgent", Color = "#FF6B6B" }],
            Phrases = [new PhraseEntry { Id = "p-1", Tags = ["opening", "urgent"] }],
        };

        var board = NewBoard(host);

        Assert.Equal(
            [new TagChipViewModel("opening", "#4CC2FF"), new TagChipViewModel("urgent", "#FF6B6B")],
            board.Phrases[0].TagChips);
    }

    [Fact]
    public void Tag_chip_colour_resolves_case_insensitively()
    {
        // The phrase stores "Opening" (its own casing); the registry has "opening" (first-used casing).
        var host = new FakePlaybackHost
        {
            Tags = [new TagInfo { Name = "opening", Color = "#4CC2FF" }],
            Phrases = [new PhraseEntry { Id = "p-1", Tags = ["Opening"] }],
        };

        var board = NewBoard(host);

        Assert.Equal("#4CC2FF", board.Phrases[0].TagChips.Single().Color);
    }

    [Fact]
    public void An_unregistered_tag_gets_an_empty_chip_colour()
    {
        // A tag on the phrase that never went through SetPhraseTags (so it has no registry entry).
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Tags = ["mystery"] }],
        };

        var board = NewBoard(host);

        Assert.Equal("", board.Phrases[0].TagChips.Single().Color);
    }

    [Fact]
    public void Editing_a_phrases_tags_refreshes_its_chips()
    {
        var host = new FakePlaybackHost
        {
            Tags = [new TagInfo { Name = "opening", Color = "#4CC2FF" }],
            Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = Category.DefaultId, Tags = [] }],
        };
        var board = NewBoard(host, showEditDialog: edit => { edit.Tags.Add("opening"); return true; });

        board.EditCommand.Execute(board.Phrases[0]);

        Assert.Equal("opening", board.Phrases[0].TagChips.Single().Name);
        Assert.Equal("#4CC2FF", board.Phrases[0].TagChips.Single().Color);
    }

    // ---- Search / filter ---------------------------------------------------------------------

    private static IEnumerable<string> VisibleTitles(BoardViewModel board) =>
        board.PhrasesView.Cast<PhraseItemViewModel>().Select(i => i.Title);

    [Fact]
    public void Search_filters_by_title_case_insensitively()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hello" }, new PhraseEntry { Id = "p-2", Title = "Goodbye" }],
        };
        var board = NewBoard(host);

        board.SearchText = "hell";

        Assert.Equal(["Hello"], VisibleTitles(board));
        Assert.False(board.SearchNoMatch);
    }

    [Fact]
    public void Search_also_matches_tags()
    {
        var host = new FakePlaybackHost
        {
            Phrases =
            [
                new PhraseEntry { Id = "p-1", Title = "A", Tags = ["greeting"] },
                new PhraseEntry { Id = "p-2", Title = "B", Tags = ["closing"] },
            ],
        };
        var board = NewBoard(host);

        board.SearchText = "greet";

        Assert.Equal(["A"], VisibleTitles(board));
    }

    [Fact]
    public void Category_filter_limits_to_the_chosen_category()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings" }],
            Phrases =
            [
                new PhraseEntry { Id = "p-1", Title = "A", CategoryId = "c-1" },
                new PhraseEntry { Id = "p-2", Title = "B", CategoryId = Category.DefaultId },
            ],
        };
        var board = NewBoard(host);

        board.SelectedCategoryFilter = board.CategoryFilterOptions.First(c => c.Id == "c-1");

        Assert.Equal(["A"], VisibleTitles(board));
    }

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
    public void Record_into_category_applies_the_category_to_the_saved_take()
    {
        var host = new FakePlaybackHost { CanRecord = true, Categories = [new Category { Id = "c-2", Name = "Closers" }] };
        var board = NewBoard(host);
        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");

        // Execute (not ExecuteAsync + await) — RecordIntoCategory stashes the pending category
        // synchronously before its first await, and awaiting the full command here would hop this
        // test off the thread that owns the Phrases CollectionView (the same WPF test artifact the
        // Save tests above avoid).
        board.RecordIntoCategoryCommand.Execute(null);
        board.PendingTake = Take();
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Greeting");
        Assert.Equal("c-2", saved.CategoryId);
    }

    [Fact]
    public void Discarding_a_take_clears_any_pending_category()
    {
        var host = new FakePlaybackHost { CanRecord = true, Categories = [new Category { Id = "c-2", Name = "Closers" }] };
        var board = NewBoard(host);
        board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");
        board.RecordIntoCategoryCommand.Execute(null); // synchronous — see comment above
        board.DiscardTakeCommand.Execute(null);

        // A later, unrelated save must NOT pick up the stale pending category.
        board.PendingTake = Take();
        board.NewTitle = "Unrelated";
        board.SaveTakeCommand.Execute(null);

        var saved = host.Phrases.Single(p => p.Title == "Unrelated");
        Assert.Equal(Category.DefaultId, saved.CategoryId);
    }

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

    [Fact]
    public void Manage_categories_opens_the_manager_then_rebuilds_the_filter_options()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = Category.DefaultId, Name = "Uncategorized" }],
        };
        // The "dialog" adds a category, like the user would.
        var board = NewBoard(host, showManageCategories: vm => { vm.NewName = "Greetings"; vm.AddCommand.Execute(null); });

        board.ManageCategoriesCommand.Execute(null);

        // "All categories" sentinel + Uncategorized + the new one.
        Assert.Contains(board.CategoryFilterOptions, c => c.Name == "Greetings");
        Assert.Same(BoardViewModel.AllCategories, board.SelectedCategoryFilter); // reset to All
    }

    [Fact]
    public void Run_setup_opens_the_wizard_with_the_current_hotkey_status()
    {
        var host = new FakePlaybackHost();
        SetupWizardViewModel? shown = null;
        var board = NewBoard(host, showSetupWizard: vm => shown = vm);

        board.RunSetupCommand.Execute(null);

        var hotkeyStep = Assert.IsType<HotkeyStatusStepViewModel>(shown!.Steps[2]);
        Assert.Equal("Global stop hotkey registered: Pause", hotkeyStep.StatusLabel);
    }

    [Fact]
    public void Run_settings_builds_a_window_view_model_from_the_hosts_and_shows_it()
    {
        var host = new FakePlaybackHost();
        SettingsWindowViewModel? shown = null;
        var board = NewBoard(host, settingsHost: new FakeSettingsHost(), showSettings: vm => shown = vm);

        board.RunSettingsCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.Equal("Pause", shown!.Behavior.HotkeyStatus.Split(": ")[1]);
    }
}
