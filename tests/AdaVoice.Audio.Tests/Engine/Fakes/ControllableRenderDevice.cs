using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Engine.Fakes;

/// <summary>
/// A render device a test can drive: it does not pull on its own — the test calls
/// <see cref="Pull"/> to act as the render thread (which also stamps the CableGate). It can
/// fault on command, and records everything it pulled.
/// </summary>
public sealed class ControllableRenderDevice : IAudioRenderDevice
{
    private readonly List<float> _captured = [];
    private ISampleProvider? _source;

    public ControllableRenderDevice(WaveFormat? format = null) => Format = format ?? TestAudio.EngineFormat;

    public WaveFormat Format { get; }
    public DeviceState State { get; private set; } = DeviceState.Stopped;

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public IReadOnlyList<float> Captured => _captured;

    /// <summary>
    /// Mirrors <c>WasapiRenderDevice.Init</c>: the real seam does not resample, so it refuses a
    /// source whose sample rate differs from the device. Keeping the fake faithful here is what
    /// lets a unit test catch the alarm-rate bug the real device would throw on.
    /// </summary>
    public void Init(ISampleProvider source)
    {
        if (source.WaveFormat.SampleRate != Format.SampleRate)
            throw new NotSupportedException(
                $"Source is {source.WaveFormat.SampleRate} Hz but the device is {Format.SampleRate} Hz.");

        _source = source;
    }

    public void Start() => State = DeviceState.Running;
    public void Stop() => State = DeviceState.Stopped;

    /// <summary>Act as the render thread: pull <paramref name="count"/> samples from the source.</summary>
    public int Pull(int count)
    {
        if (_source is null || State != DeviceState.Running)
            return 0;

        var buffer = new float[count];
        var read = _source.Read(buffer, 0, count);
        for (var i = 0; i < read; i++)
            _captured.Add(buffer[i]);
        return read;
    }

    public void Fault(Exception error)
    {
        State = DeviceState.Faulted;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(DeviceState.Faulted, error));
    }

    public void Dispose() { }
}
