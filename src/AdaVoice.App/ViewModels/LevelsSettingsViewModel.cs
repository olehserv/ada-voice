using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Settings window: the Levels group. Owns the mic-duck slider (moved here from the
/// Board's status bar — design 05 places it in Settings) and re-runs voice calibration by reusing
/// the setup wizard's <see cref="CalibrationStepViewModel"/> unchanged.</summary>
public partial class LevelsSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuckLabel))]
    private double _micDuckDb;

    public LevelsSettingsViewModel(ISettingsHost settings, ISetupHost setup)
    {
        _settings = settings;
        // Seed the backing field directly: assigning the property would fire OnMicDuckDbChanged
        // and post a needless duck change at startup.
        _micDuckDb = settings.MicDuckDb;
        Calibration = new CalibrationStepViewModel(setup);
    }

    /// <summary>The duck level as a short label, e.g. "-12 dB".</summary>
    public string DuckLabel => $"{MicDuckDb:F0} dB";

    /// <summary>Re-run voice calibration — the same step view-model and view the setup wizard
    /// uses, with its <c>CanAdvance</c> simply unused here.</summary>
    public CalibrationStepViewModel Calibration { get; }

    /// <summary>Persist the current duck level (call when the slider drag finishes).</summary>
    public void Commit() => _settings.SaveSettings();

    partial void OnMicDuckDbChanged(double value) => _settings.SetMicDuckDb(value);
}
