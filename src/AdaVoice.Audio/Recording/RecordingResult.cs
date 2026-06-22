namespace AdaVoice.Audio.Recording;

/// <summary>
/// The outcome of a recording take: the trimmed raw samples (engine format), the loudness-match
/// <see cref="GainDb"/> to store as metadata (applied at playback, not baked in), and measured
/// duration/peak. <see cref="NoSignal"/> means the take was silent after trimming.
/// </summary>
public sealed record RecordingResult(float[] Samples, double GainDb, int DurationMs, double PeakDbfs)
{
    public bool HasSignal => Samples.Length > 0;

    public static readonly RecordingResult NoSignal = new([], 0, 0, double.NegativeInfinity);
}
