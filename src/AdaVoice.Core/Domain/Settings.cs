namespace AdaVoice.Core.Domain;

/// <summary>
/// User preferences stored in <c>settings.json</c> (design 04 §1). Only the monitor-device fields
/// exist today; the rest of the design's settings are added by the slices that wire each one.
/// </summary>
public sealed record Settings
{
    /// <summary>Friendly-name substring of the output device previews play to. Null means the OS
    /// default output.</summary>
    public string? MonitorDeviceName { get; init; }

    /// <summary>When false, previews always use the OS default output regardless of
    /// <see cref="MonitorDeviceName"/>.</summary>
    public bool MonitorEnabled { get; init; } = true;
}
