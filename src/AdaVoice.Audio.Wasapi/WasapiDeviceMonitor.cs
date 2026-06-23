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
        // so dropping a role here could miss a real change on the cardinal mic path. The host dedupes
        // by device id (same id → one action). defaultDeviceId is null when there is no default.
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

    private void Raise(DeviceChangeKind kind, string deviceId) =>
        DeviceChanged?.Invoke(this, new DeviceChangeEventArgs(kind, deviceId));
}
