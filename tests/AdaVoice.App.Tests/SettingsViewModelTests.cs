using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class SettingsViewModelTests
{
    [Fact]
    public void Initializes_from_the_host_without_applying_or_saving()
    {
        var host = new FakeSettingsHost { MicDuckDb = -8 };

        var vm = new SettingsViewModel(host);

        Assert.Equal(-8, vm.MicDuckDb);
        Assert.Empty(host.SetCalls);  // no spurious apply at startup
        Assert.Equal(0, host.SaveCount);
    }

    [Fact]
    public void Changing_the_level_applies_it_live_but_does_not_save()
    {
        var host = new FakeSettingsHost { MicDuckDb = -12 };
        var vm = new SettingsViewModel(host);

        vm.MicDuckDb = -20;

        Assert.Equal([-20.0], host.SetCalls);
        Assert.Equal(0, host.SaveCount); // persisted only on Commit
    }

    [Fact]
    public void Commit_persists_the_settings()
    {
        var host = new FakeSettingsHost();
        var vm = new SettingsViewModel(host);

        vm.Commit();

        Assert.Equal(1, host.SaveCount);
    }

    [Fact]
    public void DuckLabel_shows_the_rounded_dB()
    {
        var vm = new SettingsViewModel(new FakeSettingsHost { MicDuckDb = -12 });

        Assert.Equal("-12 dB", vm.DuckLabel);
    }
}
