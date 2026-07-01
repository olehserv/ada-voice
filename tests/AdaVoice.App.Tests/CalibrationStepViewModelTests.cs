using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class CalibrationStepViewModelTests
{
    [Fact]
    public void Cannot_advance_before_calibrating()
    {
        var step = new CalibrationStepViewModel(new FakePlaybackHost());

        Assert.False(step.CanAdvance);
        Assert.True(step.CanStart);
        Assert.False(step.HasMessage);
    }

    [Fact]
    public async Task Successful_calibration_allows_advancing()
    {
        var host = new FakePlaybackHost { NextCalibrationResult = new CalibrationResult(true, 0.05, null) };
        var step = new CalibrationStepViewModel(host);

        await step.StartCalibrationCommand.ExecuteAsync(null);

        Assert.True(step.CanAdvance);
        Assert.True(step.Succeeded);
        Assert.False(step.IsRecording);
        Assert.Contains("Calibrate", host.Calls);
    }

    [Fact]
    public async Task Too_quiet_calibration_does_not_allow_advancing()
    {
        var host = new FakePlaybackHost
        {
            NextCalibrationResult = new CalibrationResult(false, 0.001, "We barely heard you — move closer to the mic and try again."),
        };
        var step = new CalibrationStepViewModel(host);

        await step.StartCalibrationCommand.ExecuteAsync(null);

        Assert.False(step.CanAdvance);
        Assert.True(step.HasMessage);
        Assert.Equal("We barely heard you — move closer to the mic and try again.", step.Result!.Message);
    }

    [Fact]
    public async Task Retrying_after_a_too_quiet_result_can_succeed()
    {
        var host = new FakePlaybackHost { NextCalibrationResult = new CalibrationResult(false, 0.001, "too quiet") };
        var step = new CalibrationStepViewModel(host);
        await step.StartCalibrationCommand.ExecuteAsync(null);
        Assert.False(step.CanAdvance);

        host.NextCalibrationResult = new CalibrationResult(true, 0.05, null); // she moved closer
        await step.StartCalibrationCommand.ExecuteAsync(null);

        Assert.True(step.CanAdvance);
    }

    [Fact]
    public async Task A_thrown_exception_surfaces_as_a_friendly_message_instead_of_crashing()
    {
        var host = new FakePlaybackHost { CalibrateThrows = true };
        var step = new CalibrationStepViewModel(host);

        await step.StartCalibrationCommand.ExecuteAsync(null);

        Assert.False(step.CanAdvance);
        Assert.False(step.IsRecording);
        Assert.True(step.HasMessage);
    }
}
