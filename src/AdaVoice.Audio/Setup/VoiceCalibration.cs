using AdaVoice.Audio.Dsp;

namespace AdaVoice.Audio.Setup;

/// <summary>Why calibration didn't succeed — the App layer's localization key, since Audio carries no
/// display text. <see cref="RecordingInProgress"/>/<see cref="CouldNotPauseCallFeed"/> are host-level
/// preconditions (set by <c>EngineHost.Calibrate</c>), not measurement outcomes, but they share this
/// enum since they're just other reasons the same <see cref="CalibrationResult"/> can fail.</summary>
public enum CalibrationFailureReason
{
    TooQuiet,
    RecordingInProgress,
    CouldNotPauseCallFeed,
}

/// <summary>Outcome of voice calibration. <see cref="MicReferenceRms"/> (linear) is meaningful only
/// when <see cref="Ok"/>; otherwise <see cref="Reason"/> explains the retry.</summary>
public sealed record CalibrationResult(bool Ok, double MicReferenceRms, CalibrationFailureReason? Reason);

/// <summary>
/// The wizard's voice-calibration step (design 05 §4 / decision #13): measure the operator's normal
/// speaking level so the recorder can loudness-match takes to it. Operates on already-trimmed samples
/// (the recorder does the capture and silence-trim, so the reference is measured exactly how takes
/// are), and rejects a too-quiet take so a bad reference is never stored.
/// </summary>
public static class VoiceCalibration
{
    /// <summary>Below this linear RMS (~ −40 dBFS) the mic is too quiet to use as a reference.</summary>
    public static readonly double MinRms = RampGain.DbToLinear(-40);

    public static CalibrationResult FromTrimmedSamples(ReadOnlySpan<float> trimmedSamples)
    {
        var rms = Loudness.Rms(trimmedSamples);
        return rms >= MinRms
            ? new CalibrationResult(true, rms, null)
            : new CalibrationResult(false, rms, CalibrationFailureReason.TooQuiet);
    }
}
