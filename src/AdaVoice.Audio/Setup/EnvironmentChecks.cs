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

/// <summary>One environment-check row: a name, pass/fail, and a human-readable detail or fix hint.</summary>
public sealed record EnvironmentCheck(string Name, CheckStatus Status, string Detail);

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
                ? Fail("Cable output", $"'{cableName}' not found — install VB-Cable.")
                : Pass("Cable output", cable.FriendlyName),

            cable is null
                ? Fail("Cable sample rate", "cable not found.")
                : cable.SampleRate == AudioFormats.SampleRate
                    ? Pass("Cable sample rate", "48 kHz")
                    : Fail("Cable sample rate", $"{cable.SampleRate} Hz — set both CABLE endpoints to 48 kHz in Sound settings."),

            defaultOutput is not null && Contains(defaultOutput.FriendlyName, cableName)
                ? Fail("Default output", $"is the cable ('{defaultOutput.FriendlyName}') — previews and the alarm would reach the call. Make speakers/headphones the Windows default.")
                : Pass("Default output", defaultOutput?.FriendlyName ?? "(none)"),

            mic is null
                ? Fail("Microphone", micName is null ? "no active microphone found." : $"'{micName}' not found.")
                : Pass("Microphone", mic.FriendlyName),
        };

        return checks;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static EnvironmentCheck Pass(string name, string detail) => new(name, CheckStatus.Pass, detail);
    private static EnvironmentCheck Fail(string name, string detail) => new(name, CheckStatus.Fail, detail);
}
