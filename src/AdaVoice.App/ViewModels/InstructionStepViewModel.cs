using AdaVoice.App.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: text instructions for pointing Chrome/Zoho's microphone at CABLE
/// Output. Pure content — no logic, always advances. No screenshots in this slice; they can be
/// added later as image assets without restructuring this step.</summary>
public sealed class InstructionStepViewModel : ObservableObject, IWizardStep
{
    public IReadOnlyList<string> Steps { get; } =
    [
        Strings.Instruction_Step1,
        Strings.Instruction_Step2,
        Strings.Instruction_Step3,
        Strings.Instruction_Step4,
    ];

    public bool CanAdvance => true;
}
