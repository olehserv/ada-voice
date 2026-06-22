namespace AdaVoice.Audio.Recording;

/// <summary>
/// Tunables for a recording take (design 06 §3 / decision #13). <see cref="ReferenceDbfs"/> stands
/// in for the wizard-calibrated <c>micReferenceRms</c> until the setup wizard exists.
/// </summary>
public sealed record RecorderOptions
{
    /// <summary>Target loudness the take is matched to (the live-mic reference level).</summary>
    public double ReferenceDbfs { get; init; } = -20;

    /// <summary>The matched take's peak may not exceed this.</summary>
    public double PeakCeilingDbfs { get; init; } = -3;

    /// <summary>Samples quieter than this at the ends are trimmed.</summary>
    public double SilenceThresholdDbfs { get; init; } = -45;

    /// <summary>Padding kept on each side of the speech after trimming.</summary>
    public int PaddingMs { get; init; } = 150;
}
