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

    [Theory]
    [InlineData(EngineState.Live, true)]
    [InlineData(EngineState.Stopped, false)]
    [InlineData(EngineState.OffAir, false)]
    [InlineData(EngineState.Degraded, false)]
    public void Is_live_only_when_live(EngineState state, bool expected)
    {
        Assert.Equal(expected, new StatusViewModel(new FakePlaybackHost { State = state }).IsLive);
    }

    // Engine control buttons: Start is enabled only when stopped; Stop engine / OFF AIR / STOP are
    // enabled whenever the engine runs (any non-stopped state), so the panic STOP stays usable off air.
    [Theory]
    [InlineData(EngineState.Stopped, true)]
    [InlineData(EngineState.Live, false)]
    [InlineData(EngineState.OffAir, false)]
    [InlineData(EngineState.Degraded, false)]
    public void Can_start_only_when_stopped(EngineState state, bool expected)
    {
        Assert.Equal(expected, new StatusViewModel(new FakePlaybackHost { State = state }).CanStart);
    }

    [Theory]
    [InlineData(EngineState.Stopped, false)]
    [InlineData(EngineState.Live, true)]
    [InlineData(EngineState.OffAir, true)]
    [InlineData(EngineState.Degraded, true)]
    public void Is_engine_running_in_every_non_stopped_state(EngineState state, bool expected)
    {
        Assert.Equal(expected, new StatusViewModel(new FakePlaybackHost { State = state }).IsEngineRunning);
    }

    [Fact]
    public void Button_flags_refresh_when_the_state_changes()
    {
        var host = new FakePlaybackHost { State = EngineState.Stopped };
        var vm = new StatusViewModel(host);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        host.RaiseStateChanged(EngineState.Live);

        Assert.Contains(nameof(StatusViewModel.CanStart), changed);
        Assert.Contains(nameof(StatusViewModel.IsEngineRunning), changed);
    }

    // The engine attaches a reason to failure transitions (e.g. "cable not at 48 kHz" on a refused
    // Start). The VM must surface it — otherwise a failed Start looks like "button does nothing".
    [Fact]
    public void State_error_is_surfaced_and_cleared_with_the_next_clean_transition()
    {
        var host = new FakePlaybackHost { State = EngineState.Stopped };
        var vm = new StatusViewModel(host);

        host.RaiseStateChanged(EngineState.Stopped, "cable not at 48 kHz");
        Assert.Equal("cable not at 48 kHz", vm.StateError);

        host.RaiseStateChanged(EngineState.Live);
        Assert.Null(vm.StateError);
    }
}
