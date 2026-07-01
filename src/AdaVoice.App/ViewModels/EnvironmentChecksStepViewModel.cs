using AdaVoice.Audio.Setup;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: runs the environment checks and gates Next on every check passing.
/// Talks only to <see cref="ISetupHost"/>, so it is unit-testable with a fake.</summary>
public partial class EnvironmentChecksStepViewModel : ObservableObject, IWizardStep
{
    private readonly ISetupHost _setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    private IReadOnlyList<EnvironmentCheck> _checks;

    public EnvironmentChecksStepViewModel(ISetupHost setup)
    {
        _setup = setup;
        _checks = setup.RunEnvironmentChecks();
    }

    /// <summary>True only when at least one check ran and every one passed.</summary>
    public bool CanAdvance => Checks.Count > 0 && Checks.All(c => c.Status == CheckStatus.Pass);

    [RelayCommand]
    private void Recheck() => Checks = _setup.RunEnvironmentChecks();
}
