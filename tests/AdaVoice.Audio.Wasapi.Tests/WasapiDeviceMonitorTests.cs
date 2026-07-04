using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Wasapi;
using NAudio.CoreAudioApi;
using NDeviceState = NAudio.CoreAudioApi.DeviceState;

namespace AdaVoice.Audio.Wasapi.Tests;

/// <summary>
/// Drives the monitor's public IMMNotificationClient methods directly — no COM registration
/// needed (Start/Stop are never called). This is "the path that actually drives recovery"
/// (review H11 item 2): a headset yank usually arrives as a state change, not OnDeviceRemoved.
/// </summary>
public class WasapiDeviceMonitorTests
{
    private static (WasapiDeviceMonitor monitor, List<DeviceChangeEventArgs> seen) NewMonitor()
    {
        var monitor = new WasapiDeviceMonitor();
        var seen = new List<DeviceChangeEventArgs>();
        monitor.DeviceChanged += (_, e) => seen.Add(e);
        return (monitor, seen);
    }

    [Fact]
    public void Device_added_and_removed_map_to_their_kinds()
    {
        var (monitor, seen) = NewMonitor();

        monitor.OnDeviceAdded("id-a");
        monitor.OnDeviceRemoved("id-b");

        Assert.Equal(2, seen.Count);
        Assert.Equal((DeviceChangeKind.Added, "id-a"), (seen[0].Kind, seen[0].DeviceId));
        Assert.Equal((DeviceChangeKind.Removed, "id-b"), (seen[1].Kind, seen[1].DeviceId));
    }

    [Theory]
    [InlineData(NDeviceState.Active, DeviceChangeKind.Added)]
    [InlineData(NDeviceState.Unplugged, DeviceChangeKind.Removed)]
    [InlineData(NDeviceState.NotPresent, DeviceChangeKind.Removed)]
    [InlineData(NDeviceState.Disabled, DeviceChangeKind.Removed)]
    public void State_changes_map_to_add_or_remove(NDeviceState state, DeviceChangeKind expected)
    {
        var (monitor, seen) = NewMonitor();

        monitor.OnDeviceStateChanged("id-x", state);

        var change = Assert.Single(seen);
        Assert.Equal(expected, change.Kind);
        Assert.Equal("id-x", change.DeviceId);
    }

    [Fact]
    public void Unmapped_states_raise_nothing()
    {
        var (monitor, seen) = NewMonitor();

        monitor.OnDeviceStateChanged("id-x", (NDeviceState)0x10 /* DEVICE_STATE_UNPLUGGED variants covered above */);

        Assert.Empty(seen);
    }

    [Fact]
    public void Default_changed_raises_per_call_and_guards_a_null_id()
    {
        var (monitor, seen) = NewMonitor();

        monitor.OnDefaultDeviceChanged(DataFlow.Render, Role.Multimedia, "id-out");
        monitor.OnDefaultDeviceChanged(DataFlow.Capture, Role.Communications, null!); // no default left

        var change = Assert.Single(seen);
        Assert.Equal(DeviceChangeKind.DefaultChanged, change.Kind);
        Assert.Equal("id-out", change.DeviceId);
    }

    // These callbacks arrive on a COM thread: a subscriber throw escaping through the CCW can
    // stop Windows delivering further notifications. The monitor must contain it.
    [Fact]
    public void A_throwing_subscriber_does_not_escape_or_block_later_events()
    {
        var (monitor, seen) = NewMonitor();
        monitor.DeviceChanged += (_, _) => throw new InvalidOperationException("bad subscriber");

        monitor.OnDeviceAdded("id-1"); // must not throw
        monitor.OnDeviceAdded("id-2");

        Assert.Equal(2, seen.Count); // earlier (well-behaved) subscribers still ran, both times
    }
}
