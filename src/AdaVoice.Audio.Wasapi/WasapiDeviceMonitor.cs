using AdaVoice.Audio.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

// NAudio has its own DeviceState (the OS endpoint state); alias it so it does not clash with our
// seam's DeviceState in AdaVoice.Audio.Abstractions.
using NDeviceState = NAudio.CoreAudioApi.DeviceState;

namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Watches Windows audio devices and reports add / remove / default-change as
/// <see cref="IDeviceMonitor.DeviceChanged"/> events, by wrapping NAudio's
/// <see cref="IMMNotificationClient"/>. It stays <b>role-agnostic</b>: it emits the raw device id,
/// and the host maps id → role (Mic/Cable/Alarm) before posting <c>DeviceChanged</c> into the
/// engine queue. Callbacks arrive on a COM thread; this class only raises an event, so it needs no
/// locking of its own.
/// </summary>
public sealed class WasapiDeviceMonitor : IDeviceMonitor, IMMNotificationClient
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private bool _started;

    public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

    public void Start()
    {
        if (_started)
            return;

        _enumerator.RegisterEndpointNotificationCallback(this);
        _started = true;
    }

    public void Stop()
    {
        if (!_started)
            return;

        _enumerator.UnregisterEndpointNotificationCallback(this);
        _started = false;
    }

    public void Dispose()
    {
        Stop();
        _enumerator.Dispose();
    }

    // --- IMMNotificationClient (invoked on a COM thread) ---

    public void OnDeviceAdded(string pwstrDeviceId) => Raise(DeviceChangeKind.Added, pwstrDeviceId);

    public void OnDeviceRemoved(string deviceId) => Raise(DeviceChangeKind.Removed, deviceId);

    public void OnDeviceStateChanged(string deviceId, NDeviceState newState)
    {
        // A headset yank usually arrives here as Unplugged, not via OnDeviceRemoved — this is the
        // path that actually drives recovery.
        if (MapState(newState) is { } kind)
            Raise(kind, deviceId);
    }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // Windows fires this up to three times per change (Console/Multimedia/Communications). We do
        // NOT filter by role: the mic is the communications default and the cable a different role,
        // so dropping a role here could miss a real change on the cardinal mic path. The duplicates
        // are harmless downstream: the engine's state guard makes a redundant DeviceChanged a no-op
        // (there is no id-dedup in the host — do not rely on one). Null id = no default exists.
        if (defaultDeviceId is not null)
            Raise(DeviceChangeKind.DefaultChanged, defaultDeviceId);
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    private static DeviceChangeKind? MapState(NDeviceState state) => state switch
    {
        NDeviceState.Active => DeviceChangeKind.Added,
        NDeviceState.Unplugged or NDeviceState.NotPresent or NDeviceState.Disabled => DeviceChangeKind.Removed,
        _ => null,
    };

    private void Raise(DeviceChangeKind kind, string deviceId)
    {
        // These run on the COM notification thread. A subscriber exception thrown back through
        // the CCW is swallowed by COM and can stop further notifications — losing e.g. the
        // device-arrived fast path and degrading recovery to the slow poll. Never let it escape.
        try
        {
            DeviceChanged?.Invoke(this, new DeviceChangeEventArgs(kind, deviceId));
        }
        catch
        {
            // Subscribers own their errors (the host already guards its handler); nothing useful
            // to do here — there is no logger on this seam, and the notification must survive.
        }
    }
}
