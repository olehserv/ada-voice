using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class AppearanceSettingsViewModelTests
{
    [Fact]
    public void Initializes_from_the_host_without_saving()
    {
        var host = new FakeSettingsHost { Theme = "light" };

        var vm = new AppearanceSettingsViewModel(host);

        Assert.Equal("light", vm.Theme);
        Assert.Equal(0, host.SaveCount);
    }

    [Fact]
    public void Selecting_a_theme_applies_and_saves_immediately()
    {
        var host = new FakeSettingsHost { Theme = "system" };
        var vm = new AppearanceSettingsViewModel(host);

        vm.Theme = "dark";

        Assert.Equal("dark", host.Theme);
        Assert.Equal(1, host.SetThemeCount);
        Assert.Equal(1, host.SaveCount);
    }
}
