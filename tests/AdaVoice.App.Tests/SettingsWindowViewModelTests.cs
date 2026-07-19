using AdaVoice.App.ViewModels;
using AdaVoice.Core.Storage;

namespace AdaVoice.App.Tests;

public class SettingsWindowViewModelTests
{
    [Fact]
    public void Builds_all_three_groups_from_the_same_settings_host()
    {
        var settings = new FakeSettingsHost { MicDuckDb = -9, AlwaysOnTop = false, Language = "uk" };
        var setup = new FakePlaybackHost();

        var vm = new SettingsWindowViewModel(
            settings, setup, "Pause",
            pickExportPath: () => null,
            pickImportFile: () => Task.FromResult<(string Path, ImportMode Mode)?>(null),
            confirmAndRestart: () => Task.CompletedTask,
            showError: _ => Task.CompletedTask,
            showInfo: _ => Task.CompletedTask);

        Assert.Equal(-9, vm.Levels.MicDuckDb);
        Assert.False(vm.Behavior.AlwaysOnTop);
        Assert.Equal("Pause", vm.Behavior.HotkeyStatus.Split(": ")[1]);
        Assert.Equal("uk", vm.Backup.Language);
    }
}
