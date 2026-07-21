using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class EnvironmentChecksStepViewModelTests
{
    private static EnvironmentCheck Pass(EnvironmentCheckKind kind) => new(kind, CheckStatus.Pass, FoundName: "ok");
    private static EnvironmentCheck Fail(EnvironmentCheckKind kind) => new(kind, CheckStatus.Fail, RequestedName: "bad");

    [Fact]
    public void Runs_checks_on_construction()
    {
        var host = new FakePlaybackHost { NextChecks = [Pass(EnvironmentCheckKind.CableOutput)] };

        var step = new EnvironmentChecksStepViewModel(host);

        Assert.Equal([EnvironmentCheckKind.CableOutput], step.Checks.Select(c => c.Kind));
        Assert.Contains("RunEnvironmentChecks", host.Calls);
    }

    [Fact]
    public void Cannot_advance_when_a_check_fails()
    {
        var host = new FakePlaybackHost
        {
            NextChecks = [Pass(EnvironmentCheckKind.CableOutput), Fail(EnvironmentCheckKind.Microphone)],
        };

        var step = new EnvironmentChecksStepViewModel(host);

        Assert.False(step.CanAdvance);
    }

    [Fact]
    public void Can_advance_when_every_check_passes()
    {
        var host = new FakePlaybackHost
        {
            NextChecks = [Pass(EnvironmentCheckKind.CableOutput), Pass(EnvironmentCheckKind.Microphone)],
        };

        var step = new EnvironmentChecksStepViewModel(host);

        Assert.True(step.CanAdvance);
    }

    [Fact]
    public void No_checks_means_cannot_advance()
    {
        var step = new EnvironmentChecksStepViewModel(new FakePlaybackHost { NextChecks = [] });

        Assert.False(step.CanAdvance);
    }

    [Fact]
    public void Recheck_re_runs_and_updates_can_advance()
    {
        var host = new FakePlaybackHost { NextChecks = [Fail(EnvironmentCheckKind.CableOutput)] };
        var step = new EnvironmentChecksStepViewModel(host);
        Assert.False(step.CanAdvance);

        host.NextChecks = [Pass(EnvironmentCheckKind.CableOutput)]; // she fixed it
        step.RecheckCommand.Execute(null);

        Assert.True(step.CanAdvance);
    }
}
