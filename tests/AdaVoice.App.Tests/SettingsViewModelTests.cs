using AdaVoice.App.ViewModels;
using AdaVoice.Host;

namespace AdaVoice.App.Tests;

public class SettingsViewModelTests
{
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
