using System.ComponentModel;
using AdaVoice.App.Resources;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Orchestrates the setup wizard: an ordered set of steps, Next/Back/SkipAnyway/Finish navigation,
/// and the completion signal the caller uses to persist "wizard completed". Each step is gated by
/// its own <see cref="IWizardStep.CanAdvance"/>; the wizard does not know what each step checks.
/// </summary>
public partial class SetupWizardViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentStep))]
    [NotifyPropertyChangedFor(nameof(IsFirstStep))]
    [NotifyPropertyChangedFor(nameof(IsLastStep))]
    [NotifyPropertyChangedFor(nameof(NextLabel))]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(ShowSkip))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    [NotifyCanExecuteChangedFor(nameof(BackCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private int _currentStepIndex;

    public SetupWizardViewModel(ISetupHost setup, string? activeHotkey)
    {
        Steps =
        [
            new EnvironmentChecksStepViewModel(setup),
            new CalibrationStepViewModel(setup),
            new HotkeyStatusStepViewModel(activeHotkey),
            new InstructionStepViewModel(),
            new FirstCallStepViewModel(),
        ];

        foreach (var step in Steps)
            step.PropertyChanged += OnStepPropertyChanged;
    }

    /// <summary>The steps, in wizard order. Fixed for the lifetime of one wizard run.</summary>
    public IReadOnlyList<IWizardStep> Steps { get; }

    /// <summary>The step currently shown. The window's ContentControl binds to this; a
    /// DataTemplate per concrete step type picks the matching view.</summary>
    public IWizardStep CurrentStep => Steps[CurrentStepIndex];

    public bool IsFirstStep => CurrentStepIndex == 0;
    public bool IsLastStep => CurrentStepIndex == Steps.Count - 1;

    /// <summary>"Step n of N" progress text (finding 8) — 1-based for display, derived from
    /// <see cref="Steps"/>'s actual count rather than a hardcoded number.</summary>
    public string StepLabel => string.Format(Strings.Wizard_StepLabelFormat, CurrentStepIndex + 1, Steps.Count);

    /// <summary>"Finish" on the last step, "Next" everywhere else.</summary>
    public string NextLabel => IsLastStep ? Strings.Wizard_Finish : Strings.Wizard_Next;

    /// <summary>True when the current step allows a normal Next/Finish.</summary>
    public bool CanAdvance => CurrentStep.CanAdvance;

    /// <summary>True when Next is blocked — "Skip anyway" is the only way forward.</summary>
    public bool ShowSkip => !CanAdvance;

    /// <summary>True once the wizard reached Finish on the last step; false on Back/Cancel/window-close.
    /// Not currently read by any caller — completion is actually persisted through
    /// <see cref="Finished"/> (<c>MainWindow.ShowSetupWizard</c> calls <c>MarkWizardCompleted()</c> when
    /// the window's <c>DialogResult</c> comes back true). Kept for a caller that wants the flag without
    /// subscribing to the event.</summary>
    public bool Completed { get; private set; }

    /// <summary>Raised when Finish is reached from the last step (via Next or SkipAnyway). The
    /// window subscribes to this to set its own DialogResult (a WPF concept this view-model does
    /// not touch).</summary>
    public event EventHandler? Finished;

    private bool CanGoBack() => !IsFirstStep;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => CurrentStepIndex--;

    [RelayCommand(CanExecute = nameof(CanAdvance))]
    private void Next()
    {
        if (IsLastStep)
            Finish();
        else
            CurrentStepIndex++;
    }

    /// <summary>Advance regardless of <see cref="CanAdvance"/> — the operator's explicit choice to
    /// proceed with a failed check or a skipped calibration.</summary>
    [RelayCommand]
    private void SkipAnyway()
    {
        if (IsLastStep)
            Finish();
        else
            CurrentStepIndex++;
    }

    private void Finish()
    {
        Completed = true;
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void OnStepPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, CurrentStep) && e.PropertyName == nameof(IWizardStep.CanAdvance))
        {
            OnPropertyChanged(nameof(CanAdvance));
            OnPropertyChanged(nameof(ShowSkip));
            NextCommand.NotifyCanExecuteChanged();
        }
    }
}
