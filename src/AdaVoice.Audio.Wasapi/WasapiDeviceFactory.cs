using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
using NAudio.CoreAudioApi;

namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Creates real WASAPI devices by role, so the engine can build and rebuild streams without ever
/// referencing WASAPI itself. A fresh <see cref="MMDevice"/> is resolved on every call, so a
/// replugged device is picked up on the next rebuild. Any failure to resolve a device becomes a
/// <b>transient</b> <see cref="AudioDeviceException"/>: the engine then keeps retrying with backoff
/// and keeps the alarm sounding, instead of giving up (design §2.2).
/// </summary>
public sealed class WasapiDeviceFactory(WasapiAudioOptions options) : IAudioDeviceFactory
{
    public IAudioCaptureDevice CreateCapture(DeviceRole role)
    {
        if (role != DeviceRole.Mic)
            throw new ArgumentException($"Capture is only available for the mic, not {role}.", nameof(role));

        var device = Resolve(role, options.MicName is null ? "default communications mic" : $"mic '{options.MicName}'",
            () => options.MicName is null
                ? WasapiDevices.DefaultCommunicationsMic()
                : WasapiDevices.FindByName(DataFlow.Capture, options.MicName));

        return new WasapiCaptureDevice(device);
    }

    public IAudioRenderDevice CreateRender(DeviceRole role) => role switch
    {
        // The cable is the virtual device Chrome reads as its mic; opt its session out of ducking.
        DeviceRole.Cable => new WasapiRenderDevice(
            Resolve(role, $"cable '{options.CableName}'", () => WasapiDevices.FindByName(DataFlow.Render, options.CableName)),
            optOutOfDucking: true),

        // The alarm is the system default output; ducking opt-out only matters for the cable.
        DeviceRole.Alarm => new WasapiRenderDevice(
            Resolve(role, "default output", WasapiDevices.DefaultRender),
            optOutOfDucking: false),

        _ => throw new ArgumentException($"Render is not available for {role}.", nameof(role)),
    };

    /// <summary>
    /// Run a resolver and turn "not found" or any WASAPI/COM error into a transient
    /// <see cref="AudioDeviceException"/>, so the engine treats it as a recoverable absence.
    /// </summary>
    private static MMDevice Resolve(DeviceRole role, string what, Func<MMDevice?> resolve)
    {
        try
        {
            return resolve() ?? throw new AudioDeviceException(
                $"Could not find the {what} for the {role} role.", isTransient: true);
        }
        catch (AudioDeviceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AudioDeviceException(
                $"Failed to resolve the {what} for the {role} role: {ex.Message}", isTransient: true);
        }
    }
}
