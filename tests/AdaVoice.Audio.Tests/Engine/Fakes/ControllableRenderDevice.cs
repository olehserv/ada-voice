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

    public WaveFormat Format => TestAudio.EngineFormat;
    public DeviceState State { get; private set; } = DeviceState.Stopped;

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public IReadOnlyList<float> Captured => _captured;

    public void Init(ISampleProvider source) => _source = source;
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
