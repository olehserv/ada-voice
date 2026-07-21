using AdaVoice.App.ViewModels;
using AdaVoice.Audio.Setup;

namespace AdaVoice.App.Tests;

public class SetupWizardViewModelTests
{
    private static SetupWizardViewModel NewWizard(FakePlaybackHost? host = null, string? hotkey = "Pause") =>
        new(host ?? new FakePlaybackHost
        {
            NextChecks = [new EnvironmentCheck(EnvironmentCheckKind.CableOutput, CheckStatus.Pass, FoundName: "ok")],
        }, hotkey);

    [Fact]
    public void Starts_on_the_first_step()
    {
        var wizard = NewWizard();

        Assert.Same(wizard.Steps[0], wizard.CurrentStep);
        Assert.True(wizard.IsFirstStep);
        Assert.False(wizard.IsLastStep);
    }

    [Fact]
    public void Has_five_steps_in_the_designed_order()
    {
        var wizard = NewWizard();

        Assert.Equal(5, wizard.Steps.Count);
        Assert.IsType<EnvironmentChecksStepViewModel>(wizard.Steps[0]);
        Assert.IsType<CalibrationStepViewModel>(wizard.Steps[1]);
        Assert.IsType<HotkeyStatusStepViewModel>(wizard.Steps[2]);
        Assert.IsType<InstructionStepViewModel>(wizard.Steps[3]);
        Assert.IsType<FirstCallStepViewModel>(wizard.Steps[4]);
    }

    [Fact]
    public void Step_label_reflects_the_current_step_and_total()
    {
        var wizard = NewWizard();

        Assert.Equal("Step 1 of 5", wizard.StepLabel);

        wizard.CurrentStepIndex = 4;

        Assert.Equal("Step 5 of 5", wizard.StepLabel);
    }

    [Fact]
    public void Next_advances_when_the_current_step_allows_it()
    {
        var wizard = NewWizard(); // checks pass by default

        wizard.NextCommand.Execute(null);

        Assert.Same(wizard.Steps[1], wizard.CurrentStep);
    }

    [Fact]
    public void Next_is_disabled_when_the_current_step_blocks_it()
    {
        var host = new FakePlaybackHost { NextChecks = [new EnvironmentCheck(EnvironmentCheckKind.CableOutput, CheckStatus.Fail, RequestedName: "missing")] };
        var wizard = NewWizard(host);

        Assert.False(wizard.NextCommand.CanExecute(null));
    }

    [Fact]
    public void Skip_anyway_advances_even_when_blocked()
    {
        var host = new FakePlaybackHost { NextChecks = [new EnvironmentCheck(EnvironmentCheckKind.CableOutput, CheckStatus.Fail, RequestedName: "missing")] };
        var wizard = NewWizard(host);
        Assert.True(wizard.ShowSkip);

        wizard.SkipAnywayCommand.Execute(null);

        Assert.Same(wizard.Steps[1], wizard.CurrentStep);
    }

    [Fact]
    public void Back_returns_to_the_previous_step_and_is_disabled_on_the_first()
    {
        var wizard = NewWizard();
        Assert.False(wizard.BackCommand.CanExecute(null));

        wizard.NextCommand.Execute(null);
        Assert.True(wizard.BackCommand.CanExecute(null));

        wizard.BackCommand.Execute(null);
        Assert.Same(wizard.Steps[0], wizard.CurrentStep);
    }

    [Fact]
    public void Next_label_reads_finish_on_the_last_step()
    {
        var wizard = NewWizard();
        Assert.Equal("Next", wizard.NextLabel);

        for (var i = 0; i < wizard.Steps.Count - 1; i++)
            wizard.SkipAnywayCommand.Execute(null);

        Assert.Equal("Finish", wizard.NextLabel);
    }

    [Fact]
    public void Reaching_next_on_the_last_step_completes_the_wizard()
    {
        var wizard = NewWizard();
        for (var i = 0; i < wizard.Steps.Count - 1; i++)
            wizard.SkipAnywayCommand.Execute(null); // fast-forward past any gates

        Assert.True(wizard.IsLastStep);
        Assert.False(wizard.Completed);

        var raised = false;
        wizard.Finished += (_, _) => raised = true;
        wizard.NextCommand.Execute(null);

        Assert.True(wizard.Completed);
        Assert.True(raised);
    }

    [Fact]
    public void Moving_between_steps_never_marks_it_completed()
    {
        var wizard = NewWizard();

        wizard.NextCommand.Execute(null); // just moves to step 2, never finishes

        Assert.False(wizard.Completed);
    }

    [Fact]
    public async Task Can_advance_updates_when_the_current_steps_own_state_changes()
    {
        var host = new FakePlaybackHost { NextCalibrationResult = new CalibrationResult(false, 0.001, CalibrationFailureReason.TooQuiet) };
        var wizard = NewWizard(host);
        wizard.NextCommand.Execute(null); // -> calibration step
        Assert.False(wizard.CanAdvance);

        host.NextCalibrationResult = new CalibrationResult(true, 0.05, null);
        await ((CalibrationStepViewModel)wizard.CurrentStep).StartCalibrationCommand.ExecuteAsync(null);

        Assert.True(wizard.CanAdvance);
        Assert.True(wizard.NextCommand.CanExecute(null));
    }
}
