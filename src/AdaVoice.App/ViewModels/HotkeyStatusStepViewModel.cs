using AdaVoice.App.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: reports the stop-hotkey that <c>MainWindow</c> already registered
/// on load. Informational only — a missing hotkey never blocks progress, since the on-screen STOP
/// button always works.</summary>
public sealed class HotkeyStatusStepViewModel : ObservableObject, IWizardStep
{
    public HotkeyStatusStepViewModel(string? activeHotkey) =>
        StatusLabel = activeHotkey is { } key
            ? string.Format(Strings.Hotkey_Registered, key)
            : Strings.Hotkey_Unavailable;

    public string StatusLabel { get; }

    public bool CanAdvance => true;
}
