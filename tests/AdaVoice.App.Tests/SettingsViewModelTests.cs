using AdaVoice.App.ViewModels;
using AdaVoice.Host;

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

    [Fact]
    public void Window_placement_reads_and_writes_through_the_host()
    {
        var host = new FakeSettingsHost { WindowPlacement = new(480, 640, 100, 80) };
        var vm = new SettingsViewModel(host);

        Assert.Equal(new WindowPlacement(480, 640, 100, 80), vm.WindowPlacement);

        vm.SaveWindowPlacement(500, 700, 120, 60);

        Assert.Equal(new WindowPlacement(500, 700, 120, 60), host.SavedPlacement);
    }

    [Fact]
    public void Wizard_completed_reads_and_writes_through_the_host()
    {
        var host = new FakeSettingsHost { WizardCompleted = false };
        var vm = new SettingsViewModel(host);
        Assert.False(vm.WizardCompleted);

        vm.MarkWizardCompleted();

        Assert.True(host.WizardCompleted);
        Assert.Equal(1, host.MarkWizardCompletedCount);
    }
}
