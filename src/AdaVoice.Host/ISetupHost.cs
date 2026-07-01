using AdaVoice.Audio.Setup;

namespace AdaVoice.Host;

/// <summary>
/// The setup wizard's view into the host: run the environment checks and run voice calibration.
/// Kept behind a seam (like <see cref="IPlaybackHost"/> / <see cref="ISettingsHost"/>) so the
/// wizard's view-models are unit-testable with a fake. <see cref="EngineHost"/> implements it.
/// </summary>
public interface ISetupHost
{
    /// <summary>Run the environment checks against the live audio devices (cable present + at
    /// 48 kHz, default output is not the cable, a mic is present).</summary>
    IReadOnlyList<EnvironmentCheck> RunEnvironmentChecks();

    /// <summary>Record <paramref name="seconds"/> of the mic, measure the reference level, and on
    /// success persist it so the recorder loudness-matches future takes to it. Blocks for the
    /// duration of the recording — callers should run it off the UI thread.</summary>
    CalibrationResult Calibrate(int seconds = 5);
}
