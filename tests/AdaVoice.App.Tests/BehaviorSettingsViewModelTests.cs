using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class BehaviorSettingsViewModelTests
{
    [Fact]
    public void Initializes_from_the_host()
    {
        var host = new FakeSettingsHost { AlwaysOnTop = false, ReplaceOnRetrigger = false };

        var vm = new BehaviorSettingsViewModel(host, "Pause");

        Assert.False(vm.AlwaysOnTop);
        Assert.False(vm.ReplaceOnRetrigger);
    }

    [Fact]
    public void Toggling_always_on_top_applies_and_saves_immediately()
    {
        var host = new FakeSettingsHost { AlwaysOnTop = true };
        var vm = new BehaviorSettingsViewModel(host, "Pause");

        vm.AlwaysOnTop = false;

        Assert.False(host.AlwaysOnTop);
        Assert.Equal(1, host.SetAlwaysOnTopCount);
        Assert.Equal(1, host.SaveCount);
    }

    [Fact]
    public void Toggling_retrigger_applies_and_saves_immediately()
    {
        var host = new FakeSettingsHost { ReplaceOnRetrigger = true };
        var vm = new BehaviorSettingsViewModel(host, "Pause");

        vm.ReplaceOnRetrigger = false;

        Assert.False(host.ReplaceOnRetrigger);
        Assert.Equal(1, host.SetReplaceOnRetriggerCount);
        Assert.Equal(1, host.SaveCount);
    }

    [Fact]
    public void Reports_the_registered_hotkey()
    {
        var vm = new BehaviorSettingsViewModel(new FakeSettingsHost(), "Pause");

        Assert.Equal("Global stop hotkey: Pause", vm.HotkeyStatus);
    }

    [Fact]
    public void Reports_unavailable_without_blocking()
    {
        var vm = new BehaviorSettingsViewModel(new FakeSettingsHost(), null);

        Assert.Equal("No global stop hotkey available — use the on-screen STOP button.", vm.HotkeyStatus);
    }
}
