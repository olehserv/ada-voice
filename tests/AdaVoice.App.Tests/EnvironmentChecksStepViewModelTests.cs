using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class EnvironmentChecksStepViewModelTests
{
    private static EnvironmentCheck Pass(string name) => new(name, CheckStatus.Pass, "ok");
    private static EnvironmentCheck Fail(string name) => new(name, CheckStatus.Fail, "bad");

    [Fact]
    public void Runs_checks_on_construction()
    {
        var host = new FakePlaybackHost { NextChecks = [Pass("Cable")] };

        var step = new EnvironmentChecksStepViewModel(host);

        Assert.Equal(["Cable"], step.Checks.Select(c => c.Name));
        Assert.Contains("RunEnvironmentChecks", host.Calls);
    }

    [Fact]
    public void Cannot_advance_when_a_check_fails()
    {
        var host = new FakePlaybackHost { NextChecks = [Pass("A"), Fail("B")] };

        var step = new EnvironmentChecksStepViewModel(host);

        Assert.False(step.CanAdvance);
    }

    [Fact]
    public void Can_advance_when_every_check_passes()
    {
        var host = new FakePlaybackHost { NextChecks = [Pass("A"), Pass("B")] };

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
        var host = new FakePlaybackHost { NextChecks = [Fail("A")] };
        var step = new EnvironmentChecksStepViewModel(host);
        Assert.False(step.CanAdvance);

        host.NextChecks = [Pass("A")]; // she fixed it
        step.RecheckCommand.Execute(null);

        Assert.True(step.CanAdvance);
    }
}
