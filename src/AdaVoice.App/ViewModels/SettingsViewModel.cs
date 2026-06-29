using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// The settings the Board exposes inline. Today: the live mic-duck level. Talks only to
/// <see cref="ISettingsHost"/>, so it is unit-testable with a fake and carries no WPF dependency.
/// </summary>
/// <remarks>
/// Changing <see cref="MicDuckDb"/> applies the level live (cheap); <see cref="Commit"/> persists it.
/// The slider calls Commit when a drag ends, so settings.json is written once, not on every tick.
/// </remarks>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DuckLabel))]
    private double _micDuckDb;

    public SettingsViewModel(ISettingsHost settings)
    {
        _settings = settings;
        // Seed the backing field directly: assigning the property would fire OnMicDuckDbChanged and
        // post a needless duck change at startup.
        _micDuckDb = settings.MicDuckDb;
    }

    /// <summary>The duck level as a short label, e.g. "-12 dB".</summary>
    public string DuckLabel => $"{MicDuckDb:F0} dB";

    /// <summary>Persist the current settings (call when a slider drag finishes).</summary>
    public void Commit() => _settings.SaveSettings();

    partial void OnMicDuckDbChanged(double value) => _settings.SetMicDuckDb(value);
}
