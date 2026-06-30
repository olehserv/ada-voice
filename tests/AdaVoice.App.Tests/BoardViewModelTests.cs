using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class BoardViewModelTests
{
    private static BoardViewModel NewBoard(
        FakePlaybackHost host,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null) =>
        new(host, host, host, new StatusViewModel(host), new SettingsViewModel(new FakeSettingsHost()),
            confirmDelete: confirmDelete, showEditDialog: showEditDialog);

    private static RecordingResult Take() => new(new float[10], GainDb: -3, DurationMs: 1000, PeakDbfs: -6);

    [Fact]
    public void Phrases_come_from_the_host()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] };

        Assert.Single(NewBoard(host).Phrases);
    }

    [Fact]
    public void Play_command_plays_that_phrase_to_the_call()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1", FileName = "p-1.wav" }] };
        var board = NewBoard(host);
        var item = board.Phrases[0];

        board.PlayCommand.Execute(item);

        Assert.Equal("PlayEntry", Assert.Single(host.Calls));
        Assert.Same(item.Entry, host.PlayedEntry);
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
    public void Start_recording_enters_recording_when_the_host_allows_it()
    {
        var board = NewBoard(new FakePlaybackHost { CanRecord = true });

        board.StartRecordingCommand.Execute(null);

        Assert.True(board.IsRecording);
    }

    [Fact]
    public void Start_recording_shows_a_notice_when_not_live()
    {
        var board = NewBoard(new FakePlaybackHost { CanRecord = false });

        board.StartRecordingCommand.Execute(null);

        Assert.False(board.IsRecording);
        Assert.NotNull(board.Notice);
    }

    [Fact]
    public void Stop_recording_with_signal_holds_a_pending_take()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);

        board.StopRecordingCommand.Execute(null);

        Assert.True(board.HasPendingTake);
        Assert.False(board.IsRecording);
    }

    [Fact]
    public void Stop_recording_with_no_signal_keeps_nothing_and_notices()
    {
        var host = new FakePlaybackHost { NextStopResult = RecordingResult.NoSignal };
        var board = NewBoard(host);

        board.StopRecordingCommand.Execute(null);

        Assert.False(board.HasPendingTake);
        Assert.NotNull(board.Notice);
    }

    [Fact]
    public void Save_take_saves_with_the_title_and_refreshes_the_board()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);
        board.StopRecordingCommand.Execute(null);
        board.NewTitle = "Greeting";

        board.SaveTakeCommand.Execute(null);

        Assert.Contains("SaveTake", host.Calls);
        Assert.Equal("Greeting", host.SavedTitle);
        Assert.False(board.HasPendingTake);
        Assert.Contains(board.Phrases, i => i.Title == "Greeting"); // appears on the board
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
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);
        board.StopRecordingCommand.Execute(null);
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
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);
        board.StopRecordingCommand.Execute(null);
        board.NewTitle = "Hello";

        string? saved = null;
        board.Saved += (_, title) => saved = title;

        board.SaveTakeCommand.Execute(null);

        Assert.Equal("Hello", saved);
    }

    [Fact]
    public void Discard_take_clears_it_without_saving()
    {
        var host = new FakePlaybackHost { NextStopResult = Take() };
        var board = NewBoard(host);
        board.StopRecordingCommand.Execute(null);

        board.DiscardTakeCommand.Execute(null);

        Assert.False(board.HasPendingTake);
        Assert.DoesNotContain("SaveTake", host.Calls);
    }

    [Fact]
    public void Preview_take_plays_the_pending_samples_to_the_monitor()
    {
        var take = Take();
        var host = new FakePlaybackHost { NextStopResult = take };
        var board = NewBoard(host);
        board.StopRecordingCommand.Execute(null);

        board.PreviewTakeCommand.Execute(null);

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
        Assert.False(board.NoMatches);
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
}
