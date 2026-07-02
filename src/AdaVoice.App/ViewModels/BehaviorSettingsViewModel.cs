using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Settings window: the Behavior group. Always-on-top and the retrigger toggle both
/// apply and save immediately (a checkbox needs no drag-end debounce, unlike the duck slider);
/// always-on-top takes effect live (the window observes it), the retrigger toggle only on the
/// next restart (it's read once when the engine builds the phrase player). The hotkey status is
/// read-only, set once at construction.</summary>
public partial class BehaviorSettingsViewModel : ObservableObject
{
    private readonly ISettingsHost _settings;

    [ObservableProperty]
    private bool _alwaysOnTop;

    [ObservableProperty]
    private bool _replaceOnRetrigger;

    public BehaviorSettingsViewModel(ISettingsHost settings, string? activeHotkey)
    {
        _settings = settings;
        _alwaysOnTop = settings.AlwaysOnTop;
        _replaceOnRetrigger = settings.ReplaceOnRetrigger;
        HotkeyStatus = activeHotkey is { } key
            ? $"Global stop hotkey: {key}"
            : "No global stop hotkey available — use the on-screen STOP button.";
    }

    /// <summary>The currently active stop hotkey, or the unavailable message. Read-only —
    /// reassignment is out of scope for this slice.</summary>
    public string HotkeyStatus { get; }

    partial void OnAlwaysOnTopChanged(bool value)
    {
        _settings.SetAlwaysOnTop(value);
        _settings.SaveSettings();
    }

    partial void OnReplaceOnRetriggerChanged(bool value)
    {
        _settings.SetReplaceOnRetrigger(value);
        _settings.SaveSettings();
    }
}
