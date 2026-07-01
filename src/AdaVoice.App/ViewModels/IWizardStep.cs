using System.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>The contract every setup-wizard step implements so the wizard shell can gate Next/
/// Finish uniformly. Content-only steps (instructions, first-call) always return true; steps with
/// a real check (environment checks, calibration) compute it. Requires
/// <see cref="INotifyPropertyChanged"/> so the wizard shell can react when a step's own state
/// changes <see cref="CanAdvance"/> (e.g. a calibration completing) — every concrete step is an
/// <c>ObservableObject</c>, which already satisfies this.</summary>
public interface IWizardStep : INotifyPropertyChanged
{
    bool CanAdvance { get; }
}
