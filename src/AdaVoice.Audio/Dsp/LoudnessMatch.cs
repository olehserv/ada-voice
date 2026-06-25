namespace AdaVoice.Audio.Dsp;

/// <summary>
/// Computes the gain (in dB) that brings a recorded take to the calibrated live-mic reference level,
/// without letting the result exceed a peak ceiling (design 06 §3 / decision #13: match
/// <c>micReferenceRms</c>, peak ceiling −3 dBFS). The gain is stored as per-phrase metadata and
/// applied at playback — it is not baked into the saved file (so re-calibration can recompute it).
/// </summary>
public static class LoudnessMatch
{
    /// <summary>
    /// Gain in dB so the take's RMS matches <paramref name="referenceRms"/> (linear), clamped so the
    /// peak never exceeds <paramref name="peakCeilingDbfs"/>. The ceiling wins over the RMS target, so
    /// loud or clipped takes stay safe. A silent take — or a missing/invalid reference — gets 0 dB.
    /// </summary>
    public static double ComputeGainDb(ReadOnlySpan<float> samples, double referenceRms, double peakCeilingDbfs = -3)
    {
        var rms = Loudness.Rms(samples);
        var peak = Loudness.Peak(samples);
        // A non-positive reference (uncalibrated, or a hand-edited settings.json) must not match:
        // referenceRms / rms would drive the gain to -inf (every phrase silent) or NaN (negative).
        if (rms <= 0 || peak <= 0 || referenceRms <= 0)
            return 0; // nothing to match

        var desired = referenceRms / rms;
        var peakLimit = RampGain.DbToLinear(peakCeilingDbfs) / peak;
        var gain = Math.Min(desired, peakLimit);

        return 20 * Math.Log10(gain);
    }
}
