using NAudio.Wave;

namespace AdaVoice.Audio.Engine;

/// <summary>
/// Sits between the mixer and the cable render. When closed (OFF AIR) it still pulls the
/// source — so the mic buffer keeps draining and the stream keeps running for the watchdog —
/// but emits silence, so nothing reaches a call. It also stamps the time of every read, which
/// the watchdog uses to detect a stalled render (AudioEngine design spec §2.1, §2.3).
/// </summary>
public sealed class CableGate : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly IEngineClock _clock;
    private volatile bool _open = true;
    private long _lastReadMs;

    public CableGate(ISampleProvider source, IEngineClock clock)
    {
        _source = source;
        _clock = clock;
        _lastReadMs = clock.NowMs; // avoid a false stall before the first real read
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>True passes audio; false (OFF AIR) emits silence.</summary>
    public bool IsOpen { get => _open; set => _open = value; }

    /// <summary>Clock time of the last read. The watchdog compares this to now.</summary>
    public long LastReadMs => Interlocked.Read(ref _lastReadMs);

    /// <summary>Restart the stall clock. Called on rebuild success: the surviving gate's stamp
    /// is from before the fault, and without a reset the watchdog would re-degrade on its next
    /// tick, before the new render thread's first pull.</summary>
    public void MarkAlive() => Interlocked.Exchange(ref _lastReadMs, _clock.NowMs);

    public int Read(float[] buffer, int offset, int count)
    {
        // Stamp before pulling the source: record the moment the render thread reached us,
        // not the moment the source returned. This is what detects a stalled render.
        Interlocked.Exchange(ref _lastReadMs, _clock.NowMs);
        var read = _source.Read(buffer, offset, count);
        if (!_open)
            Array.Clear(buffer, offset, read);
        return read;
    }
}
