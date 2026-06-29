using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Recording;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class BoardViewModelTests
{
    private static BoardViewModel NewBoard(FakePlaybackHost host) =>
        new(host, host, new StatusViewModel(host), new SettingsViewModel(new FakeSettingsHost()));

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
}
