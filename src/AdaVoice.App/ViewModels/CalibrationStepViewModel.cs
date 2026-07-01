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
    private CalibrationResult? _result;

    public CalibrationStepViewModel(ISetupHost setup) => _setup = setup;

    /// <summary>True only after a successful calibration.</summary>
    public bool CanAdvance => Result is { Ok: true };

    /// <summary>The idle "Start" button shows only while not recording.</summary>
    public bool CanStart => !IsRecording;

    /// <summary>The success message shows only after a successful calibration.</summary>
    public bool Succeeded => Result is { Ok: true };

    /// <summary>A retry/error message is present (e.g. "too quiet") and should be shown.</summary>
    public bool HasMessage => Result?.Message is not null;

    [RelayCommand]
    private async Task StartCalibration()
    {
        IsRecording = true;
        Result = null;
        try
        {
            Result = await Task.Run(() => _setup.Calibrate(5));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Result = new CalibrationResult(false, 0, "Could not access the microphone — close anything else using it and try again.");
        }
        finally
        {
            IsRecording = false;
        }
    }
}
