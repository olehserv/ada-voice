using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Settings window: the Appearance group. The theme picker applies and saves
/// immediately (a ComboBox selection needs no drag-end debounce, unlike the duck slider) and
/// takes effect live — the window observes this VM and calls <see cref="App.ApplyThemePreference"/>,
/// mirroring how <see cref="BehaviorSettingsViewModel.AlwaysOnTop"/> drives the window's
/// <c>Topmost</c>.</summary>
public partial class AppearanceSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _settings;

    [ObservableProperty]
    private string _theme;

    public AppearanceSettingsViewModel(ISettingsHost settings)
    {
        _settings = settings;
        _theme = settings.Theme;
    }

    partial void OnThemeChanged(string value)
    {
        _settings.SetTheme(value);
        _settings.SaveSettings();
    }
}
