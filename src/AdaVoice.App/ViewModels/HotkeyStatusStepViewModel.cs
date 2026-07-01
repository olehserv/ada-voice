using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: reports the stop-hotkey that <c>MainWindow</c> already registered
/// on load. Informational only — a missing hotkey never blocks progress, since the on-screen STOP
/// button always works.</summary>
public sealed class HotkeyStatusStepViewModel : ObservableObject, IWizardStep
{
    public HotkeyStatusStepViewModel(string? activeHotkey) =>
        StatusLabel = activeHotkey is { } key
            ? $"Global stop hotkey registered: {key}"
            : "No global stop hotkey available — use the on-screen STOP button.";

    public string StatusLabel { get; }

    public bool CanAdvance => true;
}
