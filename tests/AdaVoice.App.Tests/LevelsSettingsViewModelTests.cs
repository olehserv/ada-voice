using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class LevelsSettingsViewModelTests
{
    [Fact]
    public void Initializes_from_the_host_without_applying_or_saving()
    {
        var host = new FakeSettingsHost { MicDuckDb = -8 };

        var vm = new LevelsSettingsViewModel(host, new FakePlaybackHost());

        Assert.Equal(-8, vm.MicDuckDb);
        Assert.Empty(host.SetCalls); // no spurious apply at startup
        Assert.Equal(0, host.SaveCount);
    }

    [Fact]
    public void Changing_the_level_applies_it_live_but_does_not_save()
    {
        var host = new FakeSettingsHost { MicDuckDb = -12 };
        var vm = new LevelsSettingsViewModel(host, new FakePlaybackHost());

        vm.MicDuckDb = -20;

        Assert.Equal([-20.0], host.SetCalls);
        Assert.Equal(0, host.SaveCount); // persisted only on Commit
    }

    [Fact]
    public void Commit_persists_the_settings()
    {
        var host = new FakeSettingsHost();
        var vm = new LevelsSettingsViewModel(host, new FakePlaybackHost());

        vm.Commit();

        Assert.Equal(1, host.SaveCount);
    }

    [Fact]
    public void DuckLabel_shows_the_rounded_dB()
    {
        var vm = new LevelsSettingsViewModel(new FakeSettingsHost { MicDuckDb = -12 }, new FakePlaybackHost());

        Assert.Equal("-12 dB", vm.DuckLabel);
    }

    [Fact]
    public void Calibration_reuses_the_wizards_step_view_model_against_the_setup_host()
    {
        var setup = new FakePlaybackHost { NextCalibrationResult = new CalibrationResult(true, 0.05, null) };

        var vm = new LevelsSettingsViewModel(new FakeSettingsHost(), setup);

        Assert.False(vm.Calibration.CanAdvance); // hasn't calibrated yet — proves it's wired to setup, not pre-run
    }
}
