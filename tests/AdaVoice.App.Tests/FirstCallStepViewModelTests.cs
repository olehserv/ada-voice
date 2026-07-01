using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class FirstCallStepViewModelTests
{
    [Fact]
    public void Always_allows_advancing_and_has_three_checklist_items()
    {
        var step = new FirstCallStepViewModel();

        Assert.True(step.CanAdvance);
        Assert.Equal(3, step.Checklist.Count);
    }

    [Fact]
    public void Checking_an_item_does_not_affect_can_advance()
    {
        var step = new FirstCallStepViewModel();

        step.Checklist[0].IsChecked = true;

        Assert.True(step.CanAdvance); // local-only feedback, never gates
    }
}
