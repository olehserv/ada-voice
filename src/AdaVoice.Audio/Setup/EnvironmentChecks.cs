namespace AdaVoice.Audio.Setup;

/// <summary>A plain snapshot of one audio endpoint — enough for the setup checks without dragging
/// WASAPI/NAudio into the check logic. <see cref="IsDefault"/> is the Windows default for its flow
/// (Multimedia output / Communications input — the same roles the engine resolves at runtime).</summary>
public sealed record AudioEndpointInfo(string FriendlyName, int SampleRate, bool IsDefault);

/// <summary>Reads the current audio environment. The WASAPI implementation lives in the Wasapi
/// project; tests use a fake so the check logic stays pure and hardware-free.</summary>
public interface IEnvironmentProbe
{
    IReadOnlyList<AudioEndpointInfo> Outputs();
    IReadOnlyList<AudioEndpointInfo> Inputs();
}

public enum CheckStatus
{
    Pass,
    Fail,
}

/// <summary>Which environment check a row reports on — the App layer's localization key, since Audio
/// carries no display text (design: this project has no UI concerns, see CLAUDE.md).</summary>
public enum EnvironmentCheckKind
{
    CableOutput,
    CableSampleRate,
    DefaultOutput,
    Microphone,
}

/// <summary>One environment-check row: which check, pass/fail, and the raw data the App layer's
/// localized message needs — never pre-formatted text (that was the whole point of <see cref="Kind"/>
/// existing). <see cref="RequestedName"/> is the name that was searched for (only set on a name-search
/// failure); <see cref="FoundName"/> is the device actually found (pass, or <see cref="DefaultOutput"/>'s
/// fail case, which reports what the default output resolved to); <see cref="MeasuredSampleRate"/> is
/// only set for <see cref="CableSampleRate"/>'s wrong-rate failure.</summary>
public sealed record EnvironmentCheck(
    EnvironmentCheckKind Kind,
    CheckStatus Status,
    string? RequestedName = null,
    string? FoundName = null,
    int? MeasuredSampleRate = null);

/// <summary>
/// The setup wizard's environment checks (design 05 §4 / mvp-roadmap): the cable exists and is at
/// 48 kHz, the OS default output is not the cable (or previews and the alarm would reach the call),
/// and a usable microphone is present. Pure logic over an <see cref="IEnvironmentProbe"/> — the real
/// WASAPI enumeration is the hardware seam.
/// </summary>
public sealed class EnvironmentChecks(IEnvironmentProbe probe)
{
    public IReadOnlyList<EnvironmentCheck> Run(string cableName, string? micName)
    {
        var outputs = probe.Outputs();
        var inputs = probe.Inputs();
        var cable = outputs.FirstOrDefault(o => Contains(o.FriendlyName, cableName));
        var defaultOutput = outputs.FirstOrDefault(o => o.IsDefault);
        var mic = micName is null
            ? inputs.FirstOrDefault(i => i.IsDefault)
            : inputs.FirstOrDefault(i => Contains(i.FriendlyName, micName));

        var checks = new List<EnvironmentCheck>
        {
            cable is null
                ? new EnvironmentCheck(EnvironmentCheckKind.CableOutput, CheckStatus.Fail, RequestedName: cableName)
                : new EnvironmentCheck(EnvironmentCheckKind.CableOutput, CheckStatus.Pass, FoundName: cable.FriendlyName),

            cable is null
                ? new EnvironmentCheck(EnvironmentCheckKind.CableSampleRate, CheckStatus.Fail)
                : cable.SampleRate == AudioFormats.SampleRate
                    ? new EnvironmentCheck(EnvironmentCheckKind.CableSampleRate, CheckStatus.Pass)
                    : new EnvironmentCheck(EnvironmentCheckKind.CableSampleRate, CheckStatus.Fail, MeasuredSampleRate: cable.SampleRate),

            defaultOutput is not null && Contains(defaultOutput.FriendlyName, cableName)
                ? new EnvironmentCheck(EnvironmentCheckKind.DefaultOutput, CheckStatus.Fail, FoundName: defaultOutput.FriendlyName)
                : new EnvironmentCheck(EnvironmentCheckKind.DefaultOutput, CheckStatus.Pass, FoundName: defaultOutput?.FriendlyName),

            mic is null
                ? new EnvironmentCheck(EnvironmentCheckKind.Microphone, CheckStatus.Fail, RequestedName: micName)
                : new EnvironmentCheck(EnvironmentCheckKind.Microphone, CheckStatus.Pass, FoundName: mic.FriendlyName),
        };

        return checks;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
