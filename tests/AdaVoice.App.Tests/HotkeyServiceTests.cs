using AdaVoice.App.Services;

namespace AdaVoice.App.Tests;

public class HotkeyServiceTests
{
    [Fact]
    public void Register_uses_Pause_first_when_it_succeeds()
    {
        var fake = new FakeHotkeyRegistrar();
        var service = new HotkeyService(fake);

        Assert.True(service.Register());
        Assert.Equal("Pause", service.ActiveHotkey);
        Assert.Equal(["Pause"], fake.Attempts); // did not even try the fallback
    }

    [Fact]
    public void Register_falls_back_to_CtrlF12_when_Pause_is_taken()
    {
        var fake = new FakeHotkeyRegistrar { FailFor = { "Pause" } };
        var service = new HotkeyService(fake);

        Assert.True(service.Register());
        Assert.Equal("Ctrl+F12", service.ActiveHotkey);
        Assert.Equal(["Pause", "Ctrl+F12"], fake.Attempts);
    }

    [Fact]
    public void Register_reports_failure_when_all_candidates_are_taken()
    {
        var fake = new FakeHotkeyRegistrar { FailFor = { "Pause", "Ctrl+F12" } };
        var service = new HotkeyService(fake);

        Assert.False(service.Register());
        Assert.Null(service.ActiveHotkey);
        Assert.Equal(["Pause", "Ctrl+F12"], fake.Attempts);
    }

    [Fact]
    public void A_key_press_raises_StopRequested_once()
    {
        var fake = new FakeHotkeyRegistrar();
        var service = new HotkeyService(fake);
        service.Register();
        var count = 0;
        service.StopRequested += (_, _) => count++;

        fake.SimulatePress();

        Assert.Equal(1, count);
    }
}
