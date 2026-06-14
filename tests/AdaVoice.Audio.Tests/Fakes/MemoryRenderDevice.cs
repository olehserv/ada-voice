using AdaVoice.Audio.Abstractions;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Fakes;

/// <summary>
/// A fake render device. Instead of sending audio to hardware, it pulls samples from the
/// source and stores them, so a test can check what the engine produced. The test
/// controls the clock by calling <see cref="Render"/> (design 08 §1).
/// </summary>
public sealed class MemoryRenderDevice : IAudioRenderDevice
{
    private readonly List<float> _captured = [];
    private ISampleProvider? _source;

    public MemoryRenderDevice(WaveFormat format) => Format = format;

    /// <summary>A render device in the engine format (48 kHz, float, mono).</summary>
    public static MemoryRenderDevice MonoFloat48k() => new(TestAudio.EngineFormat);

    public WaveFormat Format { get; }
    public DeviceState State { get; private set; } = DeviceState.Stopped;

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    /// <summary>All samples pulled from the source so far.</summary>
    public IReadOnlyList<float> Captured => _captured;

    public void Init(ISampleProvider source) => _source = source;

    public void Start() => SetState(DeviceState.Running);
    public void Stop() => SetState(DeviceState.Stopped);

    /// <summary>
    /// Pull up to <paramref name="sampleCount"/> samples from the source and store them.
    /// Returns how many samples were really read.
    /// </summary>
    public int Render(int sampleCount)
    {
        if (_source is null)
            throw new InvalidOperationException("Call Init with a source before rendering.");
        if (State != DeviceState.Running)
            throw new InvalidOperationException("Start the device before rendering.");

        var buffer = new float[sampleCount];
        var read = _source.Read(buffer, 0, sampleCount);
        for (var i = 0; i < read; i++)
            _captured.Add(buffer[i]);
        return read;
    }

    public void Dispose() { }

    private void SetState(DeviceState state)
    {
        State = state;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(state));
    }
}
