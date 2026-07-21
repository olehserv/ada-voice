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

    [ObservableProperty]
    private bool _monitorLivePlayback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonitorVolumeLabel))]
    private int _monitorVolumePercent;

    public LevelsSettingsViewModel(ISettingsHost settings, ISetupHost setup)
    {
        _settings = settings;
        // Seed the backing fields directly: assigning the properties would fire the OnXChanged
        // partials and post needless changes at startup (a duck change, and — for the checkbox —
        // an unwanted disk write, since its OnChanged also saves).
        _micDuckDb = settings.MicDuckDb;
        _monitorLivePlayback = settings.MonitorLivePlayback;
        _monitorVolumePercent = settings.MonitorVolumePercent;
        Calibration = new CalibrationStepViewModel(setup);
    }

    /// <summary>The duck level as a short label, e.g. "-12 dB".</summary>
    public string DuckLabel => $"{MicDuckDb:F0} dB";

    /// <summary>The monitor volume as a short label, e.g. "100 %".</summary>
    public string MonitorVolumeLabel => $"{MonitorVolumePercent} %";

    /// <summary>Re-run voice calibration — the same step view-model and view the setup wizard
    /// uses, with its <c>CanAdvance</c> simply unused here.</summary>
    public CalibrationStepViewModel Calibration { get; }

    /// <summary>Persist the current duck level and monitor volume (call when a slider drag
    /// finishes).</summary>
    public void Commit() => _settings.SaveSettings();

    partial void OnMicDuckDbChanged(double value) => _settings.SetMicDuckDb(value);

    partial void OnMonitorLivePlaybackChanged(bool value)
    {
        // Checkbox — applies and saves immediately, no drag to debounce (mirrors
        // BehaviorSettingsViewModel.OnAlwaysOnTopChanged).
        _settings.SetMonitorLivePlayback(value);
        _settings.SaveSettings();
    }

    partial void OnMonitorVolumePercentChanged(int value) => _settings.SetMonitorVolumePercent(value);
}
