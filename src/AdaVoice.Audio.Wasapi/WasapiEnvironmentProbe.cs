using AdaVoice.Audio.Setup;
using NAudio.CoreAudioApi;

namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Reads the real audio environment for the setup checks. Each endpoint's format is read defensively —
/// activating a device's <c>AudioClient</c> can fail for a flaky endpoint, and one bad device must not
/// sink the whole check. The default roles match what the engine resolves at runtime (Multimedia
/// output, Communications input).
/// </summary>
public sealed class WasapiEnvironmentProbe : IEnvironmentProbe
{
    public IReadOnlyList<AudioEndpointInfo> Outputs() => Describe(DataFlow.Render, Role.Multimedia);
    public IReadOnlyList<AudioEndpointInfo> Inputs() => Describe(DataFlow.Capture, Role.Communications);

    private static IReadOnlyList<AudioEndpointInfo> Describe(DataFlow flow, Role role)
    {
        var defaultId = TryDefaultId(flow, role);
        var endpoints = new List<AudioEndpointInfo>();

        foreach (var device in WasapiDevices.Active(flow))
        {
            try
            {
                endpoints.Add(new AudioEndpointInfo(
                    device.FriendlyName, device.AudioClient.MixFormat.SampleRate, device.ID == defaultId));
            }
            catch
            {
                // A flaky endpoint (e.g. AudioClient activation fails) is skipped, not fatal.
            }
            finally
            {
                device.Dispose();
            }
        }

        return endpoints;
    }

    private static string? TryDefaultId(DataFlow flow, Role role)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
            return device.ID;
        }
        catch
        {
            return null; // no default for this flow (e.g. no devices) — checks still run
        }
    }
}
