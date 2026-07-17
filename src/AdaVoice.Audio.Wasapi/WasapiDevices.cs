using NAudio.CoreAudioApi;

namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Helpers to find audio devices by role or by name. Device enumeration lives here in the
/// WASAPI layer, not in the core. Used by the host and by the setup wizard's environment checks.
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

    public static MMDevice? FindByName(DataFlow flow, string nameSubstring) =>
        FirstOrDefaultDisposing(flow, d => d.FriendlyName.Contains(nameSubstring, StringComparison.OrdinalIgnoreCase));

    /// <summary>The active device with this endpoint id, or null. Used by the host to classify a
    /// device-monitor event's id (which flow / role it belongs to).</summary>
    public static MMDevice? ById(string id) =>
        FirstOrDefaultDisposing(DataFlow.All, d => d.ID == id);

    /// <summary>
    /// Scan the active endpoints for the first match, disposing every non-match (they are COM
    /// RCWs — a plain LINQ FirstOrDefault leaked all of them). One flaky endpoint whose property
    /// getter throws is skipped, not fatal — it must not hide a real match later in the list.
    /// </summary>
    private static MMDevice? FirstOrDefaultDisposing(DataFlow flow, Func<MMDevice, bool> matches)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? found = null;
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            var keep = false;
            try
            {
                keep = found is null && matches(device);
            }
            catch
            {
                // Flaky endpoint (e.g. FriendlyName read fails) — skip it, keep scanning.
            }

            if (keep)
                found = device;
            else
                device.Dispose();
        }

        return found;
    }
}
