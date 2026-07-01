using AdaVoice.App.ViewModels;

namespace AdaVoice.App.Tests;

public class HotkeyStatusStepViewModelTests
{
    [Fact]
    public void Reports_the_registered_hotkey()
    {
        var step = new HotkeyStatusStepViewModel("Pause");

        Assert.Equal("Global stop hotkey registered: Pause", step.StatusLabel);
        Assert.True(step.CanAdvance);
    }

    [Fact]
    public void Reports_unavailable_without_blocking()
    {
        var step = new HotkeyStatusStepViewModel(null);

        Assert.Equal("No global stop hotkey available — use the on-screen STOP button.", step.StatusLabel);
        Assert.True(step.CanAdvance);
    }
}
