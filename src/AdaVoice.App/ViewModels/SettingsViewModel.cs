using AdaVoice.Host;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Board/App-lifecycle settings: the window's remembered placement and whether the setup wizard
/// has been completed. Talks only to <see cref="ISettingsHost"/>, so it is unit-testable with a
/// fake. The mic-duck slider lives in <see cref="LevelsSettingsViewModel"/> (the Settings window)
/// instead — this class is not the Settings screen's view-model.
/// </summary>
public sealed class SettingsViewModel
{
    private readonly ISettingsHost _settings;

    public SettingsViewModel(ISettingsHost settings) => _settings = settings;

    /// <summary>The window's saved size and position, or null to use the XAML defaults (first run).</summary>
    public WindowPlacement? WindowPlacement => _settings.WindowPlacement;

    /// <summary>Remember and persist the window's size and position (called when the window closes).</summary>
    public void SaveWindowPlacement(double width, double height, double left, double top) =>
        _settings.SaveWindowPlacement(width, height, left, top);

    /// <summary>True once the setup wizard has been completed at least once.</summary>
    public bool WizardCompleted => _settings.WizardCompleted;

    /// <summary>Mark the setup wizard completed and persist immediately.</summary>
    public void MarkWizardCompleted() => _settings.MarkWizardCompleted();
}
