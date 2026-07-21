using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Engine.Fakes;

/// <summary>
/// Hands out controllable fake devices and exposes the most recently created one per role, so a
/// test can drive and inspect them. Can be told to fail the next create for a role, either
/// transiently (retry) or terminally (stop).
/// </summary>
public sealed class FakeDeviceFactory : IAudioDeviceFactory
{
    private readonly Dictionary<DeviceRole, (bool transient, string message)> _failNext = new();

    public ControllableCaptureDevice? LastMic { get; private set; }
    public ControllableRenderDevice? LastCable { get; private set; }
    public ControllableRenderDevice? LastAlarm { get; private set; }

    public int CableCreateCount { get; private set; }

    /// <summary>Format the next Alarm device reports (null = engine format). Lets a test simulate a
    /// system default output that is not 48 kHz.</summary>
    public WaveFormat? AlarmFormat { get; set; }

    /// <summary>Format the next Cable device reports (null = engine format). Lets a test simulate a
    /// cable replugged at the wrong sample rate — its Init then throws UnsupportedSampleRateException,
    /// which is NOT an AudioDeviceException (the H1 escape path).</summary>
    public WaveFormat? CableFormat { get; set; }

    /// <summary>Format the next Mic device reports (null = engine format). Lets a test simulate a
    /// multi-capsule mic with more than 2 channels — EngineFormat.Convert then throws
    /// UnsupportedChannelCountException.</summary>
    public WaveFormat? MicFormat { get; set; }

    /// <summary>Make the next create for <paramref name="role"/> throw.</summary>
    public void FailNext(DeviceRole role, bool transient, string message = "fake failure")
        => _failNext[role] = (transient, message);

    public IAudioCaptureDevice CreateCapture(DeviceRole role)
    {
        ThrowIfArmed(role);
        return LastMic = new ControllableCaptureDevice(MicFormat);
    }

    public IAudioRenderDevice CreateRender(DeviceRole role)
    {
        ThrowIfArmed(role);
        var device = role switch
        {
            DeviceRole.Alarm => new ControllableRenderDevice(AlarmFormat),
            DeviceRole.Cable => new ControllableRenderDevice(CableFormat),
            _ => new ControllableRenderDevice(),
        };
        if (role == DeviceRole.Cable) { LastCable = device; CableCreateCount++; }
        else if (role == DeviceRole.Alarm) LastAlarm = device;
        return device;
    }

    private void ThrowIfArmed(DeviceRole role)
    {
        if (!_failNext.Remove(role, out var fail))
            return;
        throw new AudioDeviceException(fail.message, fail.transient);
    }
}
