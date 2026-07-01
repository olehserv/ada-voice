using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: text instructions for pointing Chrome/Zoho's microphone at CABLE
/// Output. Pure content — no logic, always advances. No screenshots in this slice; they can be
/// added later as image assets without restructuring this step.</summary>
public sealed class InstructionStepViewModel : ObservableObject, IWizardStep
{
    public IReadOnlyList<string> Steps { get; } =
    [
        "Open Chrome and go to your call site (e.g. Zoho Meeting or Zoho Voice).",
        "Open the microphone/audio settings for the call.",
        "Set the microphone to \"CABLE Output (VB-Audio Virtual Cable)\".",
        "Continue to the next step to confirm it works with a real test call.",
    ];

    public bool CanAdvance => true;
}
