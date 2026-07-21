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

    /// <summary>The setup wizard's voice-calibration result: the live-mic reference as a <b>linear</b>
    /// RMS, which the recorder loudness-matches takes to (decision #13). Null means uncalibrated — the
    /// recorder uses its dBFS stand-in. Null (not 0) on purpose: a 0 reference would make every take
    /// silent.</summary>
    public double? MicReferenceRms { get; init; }

    /// <summary>The main window's last size and position, so it reopens where the operator left it. All
    /// four are null until the window is first closed (never saved → use the XAML defaults). Stored as
    /// plain numbers here; the host composes them into a <c>WindowPlacement</c> for the UI.</summary>
    public double? WindowWidth { get; init; }
    public double? WindowHeight { get; init; }
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }

    /// <summary>True once the setup wizard has been completed at least once. Drives whether it
    /// auto-shows on startup; false (the default) means "never completed" — show it.</summary>
    public bool WizardCompleted { get; init; }

    /// <summary>Whether the Board window stays always-on-top. The window itself applies this (a
    /// WPF concept this record does not touch) — this is just the persisted preference. Default
    /// true so an existing settings.json (this field absent) changes nothing for anyone until they
    /// explicitly turn it off — it matches the app's original hardcoded Topmost="True".</summary>
    public bool AlwaysOnTop { get; init; } = true;

    /// <summary>If true, playing a new phrase replaces the one currently playing; if false, the new
    /// trigger is ignored while a phrase is already playing. Read once when the engine builds the
    /// phrase player — changing it takes effect on the next restart. Default true, matching
    /// <c>PhrasePlayerOptions.ReplaceOnRetrigger</c>'s existing default.</summary>
    public bool ReplaceOnRetrigger { get; init; } = true;

    /// <summary>The UI language code ("en", "uk", or "pl"). Applies on restart. Default "en" — the
    /// app is English-only until the localization retrofit lands; choosing another language
    /// persists the choice but does not yet change any displayed text.</summary>
    public string Language { get; init; } = "en";

    /// <summary>Theme preference: "system" (follow the OS, default), "light", or "dark". Absent in
    /// an existing settings.json ⇒ "system", i.e. the app's original OS-follow behavior.</summary>
    public string Theme { get; init; } = "system";

    /// <summary>Whether a playing phrase is also rendered to the operator's own output (the OS
    /// default device) while it plays to the call, so the operator can confirm what the other side
    /// hears. Distinct from <see cref="MonitorEnabled"/>, which only picks the device previews use.
    /// Default true — an existing settings.json (this field absent) gets the feature on, matching
    /// the beta feedback that prompted it.</summary>
    public bool MonitorLivePlayback { get; init; } = true;

    /// <summary>Volume of the live monitor, 0-100 (100 = the same level the call hears). A
    /// percentage, not dB, since this is a plain operator-facing control. Default 100.</summary>
    public int MonitorVolumePercent { get; init; } = 100;
}
