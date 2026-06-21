using NAudio.CoreAudioApi;

namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Helpers to find audio devices by role or by name. Device enumeration lives here in the
/// WASAPI layer, not in the core. Used by the runner now, and by the setup wizard later.
/// </summary>
public static class WasapiDevices
{
    public static IReadOnlyList<MMDevice> Active(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList();
    }

    public static MMDevice DefaultCommunicationsMic()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }

    /// <summary>The system default output. Used for the DEGRADED alarm so it sounds where the
    /// operator is already listening.</summary>
    public static MMDevice DefaultRender()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public static MMDevice? FindByName(DataFlow flow, string nameSubstring)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase));
    }
}
