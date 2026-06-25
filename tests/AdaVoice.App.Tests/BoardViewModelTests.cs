using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class BoardViewModelTests
{
    private static BoardViewModel NewBoard(FakePlaybackHost host) =>
        new(host, new StatusViewModel(host));

    [Fact]
    public void Phrases_come_from_the_host()
    {
        var host = new FakePlaybackHost { Phrases = [new PhraseEntry { Id = "p-1" }] };

        Assert.Single(NewBoard(host).Phrases);
    }

    [Fact]
    public void Play_command_plays_that_phrase_to_the_call()
    {
        var host = new FakePlaybackHost();
        var entry = new PhraseEntry { Id = "p-1", FileName = "p-1.wav" };

        NewBoard(host).PlayCommand.Execute(entry);

        Assert.Equal("PlayEntry", Assert.Single(host.Calls));
        Assert.Same(entry, host.PlayedEntry);
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
}
