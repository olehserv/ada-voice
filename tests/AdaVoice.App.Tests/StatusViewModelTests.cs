using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Engine;

namespace AdaVoice.App.Tests;

public class StatusViewModelTests
{
    [Theory]
    [InlineData(EngineState.Stopped, "STOPPED")]
    [InlineData(EngineState.Live, "LIVE")]
    [InlineData(EngineState.OffAir, "OFF AIR")]
    [InlineData(EngineState.Degraded, "DEGRADED")]
    public void State_label_matches_the_engine_state(EngineState state, string expected)
    {
        var host = new FakePlaybackHost { State = state };

        var vm = new StatusViewModel(host);

        Assert.Equal(expected, vm.StateLabel);
    }

    [Fact]
    public void Label_updates_when_the_host_state_changes()
    {
        var host = new FakePlaybackHost { State = EngineState.Stopped };
        var vm = new StatusViewModel(host); // default marshal runs inline

        host.RaiseStateChanged(EngineState.Live);

        Assert.Equal(EngineState.Live, vm.State);
        Assert.Equal("LIVE", vm.StateLabel);
    }
}
