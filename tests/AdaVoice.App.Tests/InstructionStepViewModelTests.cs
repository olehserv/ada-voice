using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class InstructionStepViewModelTests
{
    [Fact]
    public void Always_allows_advancing_and_has_content()
    {
        var step = new InstructionStepViewModel();

        Assert.True(step.CanAdvance);
        Assert.NotEmpty(step.Steps);
    }
}
