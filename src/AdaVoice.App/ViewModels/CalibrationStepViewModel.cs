using AdaVoice.App.Resources;
using AdaVoice.Audio.Setup;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>Setup-wizard step: records 5 seconds of the operator's voice and stores the reference
/// level for loudness-matching future takes. <see cref="ISetupHost.Calibrate"/> blocks for the
/// recording duration, so it runs on a background thread (same pattern as
/// <see cref="BoardViewModel.TestOnHeadphonesCommand"/>).</summary>
public partial class CalibrationStepViewModel : ObservableObject, IWizardStep
{
    private readonly ISetupHost _setup;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdvance))]
    [NotifyPropertyChangedFor(nameof(Succeeded))]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    [NotifyPropertyChangedFor(nameof(Message))]
    private CalibrationResult? _result;

    // Set only by StartCalibration's catch block — a mic-access exception is an App/UI-layer
    // concern, not one of Audio's CalibrationFailureReason codes, so it rides separately from Result.
    private string? _micAccessError;

    public CalibrationStepViewModel(ISetupHost setup) => _setup = setup;

    /// <summary>True only after a successful calibration.</summary>
    public bool CanAdvance => Result is { Ok: true };

    /// <summary>The idle "Start" button shows only while not recording.</summary>
    public bool CanStart => !IsRecording;

    /// <summary>The success message shows only after a successful calibration.</summary>
    public bool Succeeded => Result is { Ok: true };

    /// <summary>A retry/error message is present (e.g. "too quiet") and should be shown.</summary>
    public bool HasMessage => Message is not null;

    /// <summary>The localized retry/error message for <see cref="Result"/>'s failure reason (Audio
    /// carries only the reason code, never display text), or the mic-access exception message.</summary>
    public string? Message => _micAccessError ?? (Result?.Reason is { } reason ? Describe(reason) : null);

    private static string Describe(CalibrationFailureReason reason) => reason switch
    {
        CalibrationFailureReason.TooQuiet => Strings.Calibration_TooQuiet,
        CalibrationFailureReason.RecordingInProgress => Strings.Calibration_AlreadyRecording,
        CalibrationFailureReason.CouldNotPauseCallFeed => Strings.Calibration_CouldNotPauseCallFeed,
        _ => "",
    };

    [RelayCommand]
    private async Task StartCalibration()
    {
        IsRecording = true;
        Result = null;
        _micAccessError = null;
        try
        {
            Result = await Task.Run(() => _setup.Calibrate(5));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _micAccessError = Strings.Calibration_MicAccessError;
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(HasMessage));
        }
        finally
        {
            IsRecording = false;
        }
    }
}
