namespace AdaVoice.Core.Domain;

/// <summary>
/// User preferences stored in <c>settings.json</c> (design 04 §1). Fields are added by the slices that
/// wire each one to a consumer; the remaining design-04 settings (stop hotkey, language, board topmost,
/// micReferenceRms) land with their Phase 4 / WPF / wizard consumers.
/// </summary>
public sealed record Settings
{
    /// <summary>Friendly-name substring of the output device previews play to. Null means the OS
    /// default output.</summary>
    public string? MonitorDeviceName { get; init; }

    /// <summary>When false, previews always use the OS default output regardless of
    /// <see cref="MonitorDeviceName"/>.</summary>
    public bool MonitorEnabled { get; init; } = true;

    /// <summary>How far the live mic is ducked while a phrase plays, in dB (negative = quieter).
    /// Applied at startup; design 04 §1 default −12.</summary>
    public double MicDuckDb { get; init; } = -12;

    /// <summary>How long the duck and un-duck ramps take, in milliseconds. Design 04 §1 default 50.</summary>
    public int DuckRampMs { get; init; } = 50;
}
