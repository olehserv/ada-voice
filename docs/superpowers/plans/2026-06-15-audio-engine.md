# AudioEngine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the `AudioEngine` orchestrator — the state machine that owns the audio graph's lifecycle and guarantees the mic is never silently dead.

**Architecture:** Single control thread + immutable command queue (design approved 2026-06-15, see `docs/superpowers/specs/2026-06-15-audio-engine-design.md`). The engine lives in the `net10.0` core, depends only on seams (`IAudioDeviceFactory`, `IEngineClock`) and the existing audio parts (`MicPassthrough`, `PhrasePlayer`, `MixingSampleProvider`), and is driven entirely by commands. Every transition is unit-tested with fakes; no real hardware in tests.

**Tech Stack:** C# / .NET 10, NAudio (`MixingSampleProvider`, `ISampleProvider`), xUnit.

**Scope:** Engine core + reliability: state machine (Stopped/Live/OffAir/Degraded), cable + alarm render streams, watchdog, rebuild + backoff, `DeviceChanged` handling, drift forwarding, and a control-thread runner. The **Wasapi device factory** is included as a build-only adapter so the engine can run on hardware. **Deferred to a later plan** (Windows adapters, hardware-validated not unit-tested, per design 08 §4): the `DeviceMonitor` `IMMNotificationClient` COM adapter that *produces* `DeviceChanged`, the host `RegisterApplicationRestart` call, and the Recorder/headphone-monitor slices.

---

## File structure

**Core — `src/AdaVoice.Audio/Engine/` (new files):**
- `DeviceRole.cs` — `enum DeviceRole { Mic, Cable, Alarm }`
- `AudioDeviceException.cs` — typed exception with `IsTransient`
- `IAudioDeviceFactory.cs` — creates capture/render devices by role
- `IEngineClock.cs` — monotonic time + periodic scheduling (pumpable in tests)
- `EngineCommand.cs` — immutable command records (in)
- `EngineEvent.cs` — immutable event records (out)
- `CableGate.cs` — silence gate + last-read stamp
- `AlarmTone.cs` — looping beep `ISampleProvider`
- `AudioEngine.cs` — the orchestrator

**Wasapi — `src/AdaVoice.Audio.Wasapi/` (new file):**
- `WasapiDeviceFactory.cs` — real factory (build-only adapter)

**Tests — `tests/AdaVoice.Audio.Tests/Engine/` (new files):**
- `Fakes/ControllableCaptureDevice.cs`
- `Fakes/ControllableRenderDevice.cs`
- `Fakes/FakeDeviceFactory.cs`
- `Fakes/ManualEngineClock.cs`
- `CableGateTests.cs`
- `AlarmToneTests.cs`
- `AudioEngineTests.cs`
- `EngineRunnerTests.cs`

---

## Task 1: Scaffolding types (enum, exception, seams, messages)

These are declarations with no behavior, so they are verified by a compile, not a unit test.

**Files:**
- Create: `src/AdaVoice.Audio/Engine/DeviceRole.cs`
- Create: `src/AdaVoice.Audio/Engine/AudioDeviceException.cs`
- Create: `src/AdaVoice.Audio/Engine/IAudioDeviceFactory.cs`
- Create: `src/AdaVoice.Audio/Engine/IEngineClock.cs`
- Create: `src/AdaVoice.Audio/Engine/EngineCommand.cs`
- Create: `src/AdaVoice.Audio/Engine/EngineEvent.cs`

- [ ] **Step 1: Create the role enum**

`src/AdaVoice.Audio/Engine/DeviceRole.cs`:
```csharp
namespace AdaVoice.Audio.Engine;

/// <summary>Which device a factory request or fault refers to.</summary>
public enum DeviceRole
{
    /// <summary>The hardware microphone (capture).</summary>
    Mic,

    /// <summary>The virtual cable input (render) Chrome uses as its mic.</summary>
    Cable,

    /// <summary>The system default output, used only to sound the DEGRADED alarm.</summary>
    Alarm,
}
```

- [ ] **Step 2: Create the typed exception**

`src/AdaVoice.Audio/Engine/AudioDeviceException.cs`:
```csharp
namespace AdaVoice.Audio.Engine;

/// <summary>
/// Thrown by an <see cref="IAudioDeviceFactory"/> when it cannot create a device.
/// <see cref="IsTransient"/> tells the engine whether to keep retrying (device busy or
/// absent) or to give up and stop (a non-recoverable configuration error).
/// </summary>
public sealed class AudioDeviceException(string message, bool isTransient) : Exception(message)
{
    public bool IsTransient { get; } = isTransient;
}
```

- [ ] **Step 3: Create the device factory seam**

`src/AdaVoice.Audio/Engine/IAudioDeviceFactory.cs`:
```csharp
using AdaVoice.Audio.Abstractions;

namespace AdaVoice.Audio.Engine;

/// <summary>
/// Creates audio devices on demand so the engine can rebuild a stream after a failure
/// without referencing WASAPI. The real implementation lives in the Wasapi project; tests
/// provide a fake. Throws <see cref="AudioDeviceException"/> on failure.
/// </summary>
public interface IAudioDeviceFactory
{
    IAudioCaptureDevice CreateCapture(DeviceRole role);
    IAudioRenderDevice CreateRender(DeviceRole role);
}
```

- [ ] **Step 4: Create the clock seam**

`src/AdaVoice.Audio/Engine/IEngineClock.cs`:
```csharp
namespace AdaVoice.Audio.Engine;

/// <summary>
/// Monotonic time plus periodic scheduling. Hidden behind a seam so tests can control time
/// (watchdog timeouts) and fire ticks by hand instead of sleeping.
/// </summary>
public interface IEngineClock
{
    /// <summary>A monotonically increasing millisecond counter (not wall-clock time).</summary>
    long NowMs { get; }

    /// <summary>Call <paramref name="callback"/> every <paramref name="intervalMs"/>. Dispose to stop.</summary>
    IDisposable SchedulePeriodic(int intervalMs, Action callback);
}
```

- [ ] **Step 5: Create the command messages**

`src/AdaVoice.Audio/Engine/EngineCommand.cs`:
```csharp
using AdaVoice.Audio.Playback;

namespace AdaVoice.Audio.Engine;

/// <summary>Immutable messages fed into the engine's command queue (design §2.3).</summary>
public abstract record EngineCommand
{
    public sealed record Start : EngineCommand;
    public sealed record Stop : EngineCommand;
    public sealed record Play(Phrase Phrase) : EngineCommand;
    public sealed record StopPhrase : EngineCommand;
    public sealed record EnterOffAir : EngineCommand;
    public sealed record ExitOffAir : EngineCommand;

    /// <summary>A device was added or removed (from the device monitor).</summary>
    public sealed record DeviceChanged(DeviceRole Role, bool Arrived) : EngineCommand;

    /// <summary>A live stream raised an error.</summary>
    public sealed record StreamFaulted(DeviceRole Role, Exception? Error) : EngineCommand;

    /// <summary>Periodic watchdog/rebuild tick.</summary>
    public sealed record WatchdogTick : EngineCommand;
}
```

- [ ] **Step 6: Create the event messages**

`src/AdaVoice.Audio/Engine/EngineEvent.cs`:
```csharp
using AdaVoice.Audio.Passthrough;

namespace AdaVoice.Audio.Engine;

/// <summary>Immutable notifications the engine raises out (design §2.3). The host marshals
/// these to the UI thread and logs them; the engine itself does no logging.</summary>
public abstract record EngineEvent
{
    public sealed record StateChanged(EngineState State, string? Error = null) : EngineEvent;
    public sealed record DriftLogged(DriftKind Kind) : EngineEvent;
    public sealed record RebuildResult(DeviceRole Role, bool Success, int Attempt) : EngineEvent;
}
```

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build src/AdaVoice.Audio/AdaVoice.Audio.csproj -c Release`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add src/AdaVoice.Audio/Engine/
git commit -m "feat(audio): add AudioEngine scaffolding types (roles, seams, messages)"
```

---

## Task 2: CableGate (OFF AIR silence + watchdog stamp)

**Files:**
- Create: `src/AdaVoice.Audio/Engine/CableGate.cs`
- Test: `tests/AdaVoice.Audio.Tests/Engine/CableGateTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/AdaVoice.Audio.Tests/Engine/CableGateTests.cs`:
```csharp
using AdaVoice.Audio;
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Tests.Engine.Fakes;
using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests.Engine;

public class CableGateTests
{
    [Fact]
    public void Open_gate_passes_samples_through()
    {
        var clock = new ManualEngineClock();
        var gate = new CableGate(ArraySampleProvider.Mono48k([0.5f, -0.5f, 1f]), clock);

        var buffer = new float[3];
        var read = gate.Read(buffer, 0, 3);

        Assert.Equal(3, read);
        Assert.Equal([0.5f, -0.5f, 1f], buffer);
    }

    [Fact]
    public void Closed_gate_outputs_silence_but_still_pulls()
    {
        var clock = new ManualEngineClock();
        var source = ArraySampleProvider.Mono48k([0.5f, -0.5f, 1f]);
        var gate = new CableGate(source, clock) { IsOpen = false };

        var buffer = new float[3];
        var read = gate.Read(buffer, 0, 3);

        Assert.Equal(3, read);               // still pulled the source (drains the mic buffer)
        Assert.Equal([0f, 0f, 0f], buffer);  // but emitted silence
    }

    [Fact]
    public void Read_stamps_the_last_read_time_from_the_clock()
    {
        var clock = new ManualEngineClock { NowMs = 1234 };
        var gate = new CableGate(ArraySampleProvider.Mono48k([0f, 0f]), clock);

        gate.Read(new float[2], 0, 2);

        Assert.Equal(1234, gate.LastReadMs);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter CableGateTests`
Expected: FAIL — `CableGate` and `ManualEngineClock` do not exist yet.

- [ ] **Step 3: Create ManualEngineClock (test fake needed by the test)**

`tests/AdaVoice.Audio.Tests/Engine/Fakes/ManualEngineClock.cs`:
```csharp
using AdaVoice.Audio.Engine;

namespace AdaVoice.Audio.Tests.Engine.Fakes;

/// <summary>A clock the test fully controls. Time only moves when the test sets it, and
/// periodic callbacks fire only when the test calls <see cref="FireTicks"/>.</summary>
public sealed class ManualEngineClock : IEngineClock
{
    private readonly List<Action> _callbacks = [];

    public long NowMs { get; set; }

    public void Advance(long ms) => NowMs += ms;

    public IDisposable SchedulePeriodic(int intervalMs, Action callback)
    {
        _callbacks.Add(callback);
        return new Stop(() => _callbacks.Remove(callback));
    }

    /// <summary>Fire every scheduled periodic callback once (simulates one timer tick).</summary>
    public void FireTicks()
    {
        foreach (var cb in _callbacks.ToArray())
            cb();
    }

    private sealed class Stop(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
```

- [ ] **Step 4: Implement CableGate**

`src/AdaVoice.Audio/Engine/CableGate.cs`:
```csharp
using NAudio.Wave;

namespace AdaVoice.Audio.Engine;

/// <summary>
/// Sits between the mixer and the cable render. When closed (OFF AIR) it still pulls the
/// source — so the mic buffer keeps draining and the stream keeps running for the watchdog —
/// but emits silence, so nothing reaches a call. It also stamps the time of every read, which
/// the watchdog uses to detect a stalled render (design §2.1, §2.3).
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

    public int Read(float[] buffer, int offset, int count)
    {
        Interlocked.Exchange(ref _lastReadMs, _clock.NowMs);
        var read = _source.Read(buffer, offset, count);
        if (!_open)
            Array.Clear(buffer, offset, read);
        return read;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter CableGateTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.Audio/Engine/CableGate.cs tests/AdaVoice.Audio.Tests/Engine/
git commit -m "feat(audio): add CableGate (OFF AIR silence + watchdog stamp)"
```

---

## Task 3: AlarmTone (audible DEGRADED beep)

**Files:**
- Create: `src/AdaVoice.Audio/Engine/AlarmTone.cs`
- Test: `tests/AdaVoice.Audio.Tests/Engine/AlarmToneTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/AdaVoice.Audio.Tests/Engine/AlarmToneTests.cs`:
```csharp
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests.Engine;

public class AlarmToneTests
{
    [Fact]
    public void Produces_a_repeating_non_silent_signal()
    {
        var tone = new AlarmTone(TestAudio.EngineFormat);

        var buffer = new float[48_000]; // 1 second
        var read = tone.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);                 // never ends
        Assert.Contains(buffer, s => Math.Abs(s) > 0.1f);  // is audible
        Assert.Contains(buffer, s => s == 0f);             // beeps (has gaps), not a flat tone
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter AlarmToneTests`
Expected: FAIL — `AlarmTone` does not exist.

- [ ] **Step 3: Implement AlarmTone**

`src/AdaVoice.Audio/Engine/AlarmTone.cs`:
```csharp
using NAudio.Wave;

namespace AdaVoice.Audio.Engine;

/// <summary>
/// An endless beeping tone for the DEGRADED alarm: 880 Hz on for 300 ms, off for 300 ms,
/// repeating. Loud and obviously wrong, so the operator cannot miss that the mic is down.
/// </summary>
public sealed class AlarmTone(WaveFormat format) : ISampleProvider
{
    private const double FreqHz = 880;
    private const float Amplitude = 0.6f;
    private long _n;

    public WaveFormat WaveFormat => format;

    public int Read(float[] buffer, int offset, int count)
    {
        var rate = WaveFormat.SampleRate;
        var halfCycle = rate * 3 / 10; // 300 ms in samples

        for (var i = 0; i < count; i++)
        {
            var onPhase = _n / halfCycle % 2 == 0;
            buffer[offset + i] = onPhase
                ? (float)(Amplitude * Math.Sin(2 * Math.PI * FreqHz * _n / rate))
                : 0f;
            _n++;
        }

        return count; // endless
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter AlarmToneTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.Audio/Engine/AlarmTone.cs tests/AdaVoice.Audio.Tests/Engine/AlarmToneTests.cs
git commit -m "feat(audio): add AlarmTone for the DEGRADED alarm"
```

---

## Task 4: Engine test fakes (controllable devices + factory)

Test infrastructure used by every `AudioEngineTests` case. No standalone test; exercised in Task 5+.

**Files:**
- Create: `tests/AdaVoice.Audio.Tests/Engine/Fakes/ControllableCaptureDevice.cs`
- Create: `tests/AdaVoice.Audio.Tests/Engine/Fakes/ControllableRenderDevice.cs`
- Create: `tests/AdaVoice.Audio.Tests/Engine/Fakes/FakeDeviceFactory.cs`

- [ ] **Step 1: Create the controllable capture device**

`tests/AdaVoice.Audio.Tests/Engine/Fakes/ControllableCaptureDevice.cs`:
```csharp
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Engine.Fakes;

/// <summary>A capture device a test can drive: push samples, and fault on command.</summary>
public sealed class ControllableCaptureDevice : IAudioCaptureDevice
{
    public WaveFormat Format => TestAudio.EngineFormat;
    public DeviceState State { get; private set; } = DeviceState.Stopped;

    public event EventHandler<CaptureBufferEventArgs>? DataAvailable;
    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public void Start() => State = DeviceState.Running;
    public void Stop() => State = DeviceState.Stopped;

    /// <summary>Push one block of mono float samples, as if the mic produced them.</summary>
    public void Push(float[] samples)
    {
        var bytes = TestAudio.ToBytes(samples);
        DataAvailable?.Invoke(this, new CaptureBufferEventArgs(bytes, bytes.Length));
    }

    /// <summary>Simulate a driver/device failure.</summary>
    public void Fault(Exception error)
    {
        State = DeviceState.Faulted;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(DeviceState.Faulted, error));
    }

    public void Dispose() { }
}
```

- [ ] **Step 2: Create the controllable render device**

`tests/AdaVoice.Audio.Tests/Engine/Fakes/ControllableRenderDevice.cs`:
```csharp
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
```

- [ ] **Step 3: Create the fake factory**

`tests/AdaVoice.Audio.Tests/Engine/Fakes/FakeDeviceFactory.cs`:
```csharp
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;

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

    /// <summary>Make the next create for <paramref name="role"/> throw.</summary>
    public void FailNext(DeviceRole role, bool transient, string message = "fake failure")
        => _failNext[role] = (transient, message);

    public IAudioCaptureDevice CreateCapture(DeviceRole role)
    {
        ThrowIfArmed(role);
        return LastMic = new ControllableCaptureDevice();
    }

    public IAudioRenderDevice CreateRender(DeviceRole role)
    {
        ThrowIfArmed(role);
        var device = new ControllableRenderDevice();
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
```

- [ ] **Step 4: Build the test project to verify the fakes compile**

Run: `dotnet build tests/AdaVoice.Audio.Tests/AdaVoice.Audio.Tests.csproj -c Release`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add tests/AdaVoice.Audio.Tests/Engine/Fakes/
git commit -m "test(audio): add controllable device fakes and fake factory for the engine"
```

---

## Task 5: AudioEngine — Start → Live (the foundation)

This task creates `AudioEngine` with its fields, constants, queue, the command dispatch (`Handle`), `DrainPending`, `Post`, the public API stubs that only enqueue, and the `Start` transition. Later tasks add one handler each.

**Files:**
- Create: `src/AdaVoice.Audio/Engine/AudioEngine.cs`
- Test: `tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`:
```csharp
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Tests.Engine.Fakes;

namespace AdaVoice.Audio.Tests.Engine;

public class AudioEngineTests
{
    private static (AudioEngine engine, FakeDeviceFactory factory, ManualEngineClock clock, List<EngineEvent> events) NewEngine()
    {
        var factory = new FakeDeviceFactory();
        var clock = new ManualEngineClock();
        var engine = new AudioEngine(factory, clock);
        var events = new List<EngineEvent>();
        engine.Events += (_, e) => events.Add(e);
        return (engine, factory, clock, events);
    }

    [Fact]
    public void Start_opens_devices_and_goes_live()
    {
        var (engine, factory, _, events) = NewEngine();

        engine.Start();
        engine.DrainPending();

        Assert.Equal(EngineState.Live, engine.State);
        Assert.NotNull(factory.LastMic);
        Assert.NotNull(factory.LastCable);
        Assert.Contains(events, e => e is EngineEvent.StateChanged { State: EngineState.Live });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter AudioEngineTests`
Expected: FAIL — `AudioEngine` does not exist.

- [ ] **Step 3: Implement AudioEngine with Start**

`src/AdaVoice.Audio/Engine/AudioEngine.cs`:
```csharp
using System.Collections.Concurrent;
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Passthrough;
using AdaVoice.Audio.Playback;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Audio.Engine;

/// <summary>
/// Owns the audio graph's lifecycle and state. Driven by a single command queue processed on
/// one thread (see <see cref="DrainPending"/> / <see cref="TryProcessNext"/>), so all state
/// transitions and stream open/close happen serialized — no locks in the core logic. Design:
/// docs/superpowers/specs/2026-06-15-audio-engine-design.md.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const int WatchdogIntervalMs = 100;
    private const int WatchdogStallMs = 500;
    private static readonly int[] BackoffMs = [250, 500, 1000, 2000, 5000];

    private readonly IAudioDeviceFactory _factory;
    private readonly IEngineClock _clock;
    private readonly PhrasePlayerOptions? _playerOptions;
    private readonly BlockingCollection<EngineCommand> _queue = new();

    // Live graph (null when Stopped).
    private IAudioCaptureDevice? _capture;
    private IAudioRenderDevice? _cableRender;
    private IAudioRenderDevice? _alarmRender;
    private MicPassthrough? _passthrough;
    private MixingSampleProvider? _mixer;
    private PhrasePlayer? _player;
    private CableGate? _gate;
    private IDisposable? _watchdog;

    // Degraded bookkeeping.
    private EngineState _stateToRestore = EngineState.Live;
    private DeviceRole _faultRole;
    private int _attempt;
    private long _nextAttemptMs;

    public AudioEngine(IAudioDeviceFactory factory, IEngineClock clock, PhrasePlayerOptions? playerOptions = null)
    {
        _factory = factory;
        _clock = clock;
        _playerOptions = playerOptions;
    }

    /// <summary>The current state. Updated on the control thread; safe to read for display.</summary>
    public EngineState State { get; private set; } = EngineState.Stopped;

    /// <summary>Raised on the control thread for every state change, drift event, and rebuild.</summary>
    public event EventHandler<EngineEvent>? Events;

    // Public API: each call only enqueues a command and returns.
    public void Start() => Post(new EngineCommand.Start());
    public void Stop() => Post(new EngineCommand.Stop());
    public void Play(Phrase phrase) => Post(new EngineCommand.Play(phrase));
    public void StopPhrase() => Post(new EngineCommand.StopPhrase());
    public void EnterOffAir() => Post(new EngineCommand.EnterOffAir());
    public void ExitOffAir() => Post(new EngineCommand.ExitOffAir());

    public void Post(EngineCommand command) => _queue.Add(command);

    /// <summary>Process every queued command now, on the calling thread. Used by tests and the runner.</summary>
    public void DrainPending()
    {
        while (_queue.TryTake(out var command))
            Handle(command);
    }

    /// <summary>Block up to <paramref name="timeoutMs"/> for one command, process it. Used by the runner.</summary>
    public bool TryProcessNext(int timeoutMs)
    {
        if (!_queue.TryTake(out var command, timeoutMs))
            return false;
        Handle(command);
        return true;
    }

    private void Handle(EngineCommand command)
    {
        switch (command)
        {
            case EngineCommand.Start: HandleStart(); break;
            // more cases added in later tasks
        }
    }

    private void HandleStart()
    {
        if (State != EngineState.Stopped)
            return;

        try
        {
            BuildGraph();
            SetState(EngineState.Live);
        }
        catch (AudioDeviceException ex) when (!ex.IsTransient)
        {
            GoStopped(ex.Message);
        }
        catch (Exception)
        {
            _stateToRestore = EngineState.Live;
            EnterDegraded(DeviceRole.Cable);
        }
    }

    private void BuildGraph()
    {
        _capture = _factory.CreateCapture(DeviceRole.Mic);
        _capture.StateChanged += OnCaptureStateChanged;

        _passthrough = new MicPassthrough(_capture);
        _passthrough.Drift += OnDrift;

        _mixer = new MixingSampleProvider(AudioFormats.Engine) { ReadFully = true };
        _mixer.AddMixerInput(_passthrough.Output);

        _player = new PhrasePlayer(_mixer, _passthrough, _playerOptions);
        _gate = new CableGate(_mixer, _clock);

        _cableRender = _factory.CreateRender(DeviceRole.Cable);
        _cableRender.StateChanged += OnCableStateChanged;
        _cableRender.Init(_gate);
        _cableRender.Start();
        _capture.Start();

        _watchdog = _clock.SchedulePeriodic(WatchdogIntervalMs, () => Post(new EngineCommand.WatchdogTick()));
    }

    private void OnCaptureStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        if (e.State == DeviceState.Faulted)
            Post(new EngineCommand.StreamFaulted(DeviceRole.Mic, e.Error));
    }

    private void OnCableStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        if (e.State == DeviceState.Faulted)
            Post(new EngineCommand.StreamFaulted(DeviceRole.Cable, e.Error));
    }

    private void OnDrift(object? sender, DriftEventArgs e)
        => Raise(new EngineEvent.DriftLogged(e.Kind));

    private void SetState(EngineState state, string? error = null)
    {
        State = state;
        Raise(new EngineEvent.StateChanged(state, error));
    }

    private void Raise(EngineEvent e) => Events?.Invoke(this, e);

    // Placeholder bodies wired up in later tasks; declared here so the file is whole.
    private void EnterDegraded(DeviceRole role) => throw new NotImplementedException();
    private void GoStopped(string? error) => throw new NotImplementedException();

    public void Dispose()
    {
        _watchdog?.Dispose();
        _capture?.Dispose();
        _cableRender?.Dispose();
        _alarmRender?.Dispose();
        _passthrough?.Dispose();
        _player?.Dispose();
        _queue.Dispose();
    }
}
```

> Note: `EnterDegraded` and `GoStopped` are implemented in Task 8 and Task 6 respectively. The `HandleStart` catch-paths that call them are not reachable in this task's test (the fake factory succeeds), so the throw is never hit here. They are replaced with real bodies before any test exercises them.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter "AudioEngineTests.Start_opens_devices_and_goes_live"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.Audio/Engine/AudioEngine.cs tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs
git commit -m "feat(audio): AudioEngine Start -> Live with command queue"
```

---

## Task 6: Stop → Stopped (and GoStopped)

**Files:**
- Modify: `src/AdaVoice.Audio/Engine/AudioEngine.cs`
- Test: `tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `AudioEngineTests`:
```csharp
[Fact]
public void Stop_tears_down_and_goes_stopped()
{
    var (engine, _, _, events) = NewEngine();
    engine.Start();
    engine.DrainPending();

    engine.Stop();
    engine.DrainPending();

    Assert.Equal(EngineState.Stopped, engine.State);
    Assert.Contains(events, e => e is EngineEvent.StateChanged { State: EngineState.Stopped });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "AudioEngineTests.Stop_tears_down_and_goes_stopped"`
Expected: FAIL — `Stop` command is not handled (no `case`).

- [ ] **Step 3: Add the Stop case and replace GoStopped**

In `Handle`, add the case:
```csharp
            case EngineCommand.Stop: GoStopped(null); break;
```

Replace the placeholder `GoStopped` with:
```csharp
    private void GoStopped(string? error)
    {
        TearDown();
        SetState(EngineState.Stopped, error);
    }

    private void TearDown()
    {
        _watchdog?.Dispose();
        _watchdog = null;
        SilenceAlarm();
        _capture?.Stop();
        _capture?.Dispose();
        _cableRender?.Stop();
        _cableRender?.Dispose();
        _passthrough?.Dispose();
        _player?.Dispose();
        _capture = null;
        _cableRender = null;
        _passthrough = null;
        _mixer = null;
        _player = null;
        _gate = null;
    }

    private void SilenceAlarm()
    {
        _alarmRender?.Stop();
        _alarmRender?.Dispose();
        _alarmRender = null;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AudioEngineTests`
Expected: PASS (Start and Stop tests).

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.Audio/Engine/AudioEngine.cs tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs
git commit -m "feat(audio): AudioEngine Stop -> Stopped with teardown"
```

---

## Task 7: Play / StopPhrase (and ignore while not Live)

**Files:**
- Modify: `src/AdaVoice.Audio/Engine/AudioEngine.cs`
- Test: `tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AudioEngineTests` (note the `using` for `Phrase`):
```csharp
[Fact]
public void Play_while_live_starts_a_phrase()
{
    var (engine, factory, _, _) = NewEngine();
    engine.Start();
    engine.DrainPending();

    engine.Play(new Phrase("p1", new float[480]));
    engine.DrainPending();

    Assert.Equal("p1", engine.ActivePhraseId);
}

[Fact]
public void Play_while_stopped_is_ignored()
{
    var (engine, _, _, _) = NewEngine();

    engine.Play(new Phrase("p1", new float[480]));
    engine.DrainPending();

    Assert.Null(engine.ActivePhraseId);
}
```

Add `using AdaVoice.Audio.Playback;` to the test file's usings.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "AudioEngineTests.Play"`
Expected: FAIL — `ActivePhraseId` and the `Play`/`StopPhrase` cases do not exist.

- [ ] **Step 3: Add ActivePhraseId and the Play/StopPhrase cases**

Add the property near `State`:
```csharp
    /// <summary>The phrase playing now, or null. For tests and the UI.</summary>
    public string? ActivePhraseId => _player?.ActivePhraseId;
```

In `Handle`, add cases:
```csharp
            case EngineCommand.Play play: HandlePlay(play.Phrase); break;
            case EngineCommand.StopPhrase: HandleStopPhrase(); break;
```

Add the handlers:
```csharp
    private void HandlePlay(Phrase phrase)
    {
        if (State == EngineState.Live)
            _player?.Play(phrase);
        // Ignored in any other state (OFF AIR pauses the cable; Degraded/Stopped have no graph).
    }

    private void HandleStopPhrase()
    {
        if (State == EngineState.Live)
            _player?.Stop();
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AudioEngineTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.Audio/Engine/AudioEngine.cs tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs
git commit -m "feat(audio): AudioEngine Play/StopPhrase (Live only)"
```

---

## Task 8: OFF AIR (gate silences the cable)

**Files:**
- Modify: `src/AdaVoice.Audio/Engine/AudioEngine.cs`
- Test: `tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `AudioEngineTests`:
```csharp
[Fact]
public void OffAir_silences_the_cable_then_resumes()
{
    var (engine, factory, _, _) = NewEngine();
    engine.Start();
    engine.DrainPending();
    factory.LastMic!.Push(Enumerable.Repeat(0.5f, 480).ToArray()); // mic has signal

    engine.EnterOffAir();
    engine.DrainPending();
    Assert.Equal(EngineState.OffAir, engine.State);

    factory.LastCable!.Pull(480);                 // render thread pulls while OFF AIR
    Assert.All(factory.LastCable.Captured, s => Assert.Equal(0f, s)); // all silence

    engine.ExitOffAir();
    engine.DrainPending();
    Assert.Equal(EngineState.Live, engine.State);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "AudioEngineTests.OffAir"`
Expected: FAIL — `EnterOffAir`/`ExitOffAir` cases do not exist.

- [ ] **Step 3: Add the OFF AIR cases**

In `Handle`, add:
```csharp
            case EngineCommand.EnterOffAir: HandleEnterOffAir(); break;
            case EngineCommand.ExitOffAir: HandleExitOffAir(); break;
```

Add the handlers:
```csharp
    private void HandleEnterOffAir()
    {
        if (State != EngineState.Live || _gate is null)
            return;
        _gate.IsOpen = false;
        SetState(EngineState.OffAir);
    }

    private void HandleExitOffAir()
    {
        if (State != EngineState.OffAir || _gate is null)
            return;
        _gate.IsOpen = true;
        SetState(EngineState.Live);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AudioEngineTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.Audio/Engine/AudioEngine.cs tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs
git commit -m "feat(audio): AudioEngine OFF AIR via cable gate"
```

---

## Task 9: Fault → Degraded + alarm; rebuild → restore

This implements `EnterDegraded`, the rebuild logic, and the alarm. It handles `StreamFaulted` and, on a watchdog tick while Degraded, retries the rebuild.

**Files:**
- Modify: `src/AdaVoice.Audio/Engine/AudioEngine.cs`
- Test: `tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AudioEngineTests`:
```csharp
[Fact]
public void Cable_fault_goes_degraded_and_raises_the_alarm()
{
    var (engine, factory, _, events) = NewEngine();
    engine.Start();
    engine.DrainPending();

    factory.LastCable!.Fault(new InvalidOperationException("device lost"));
    engine.DrainPending();

    Assert.Equal(EngineState.Degraded, engine.State);
    Assert.NotNull(factory.LastAlarm);                       // alarm device created
    Assert.Equal(DeviceState.Running, factory.LastAlarm!.State);
    Assert.Contains(events, e => e is EngineEvent.StateChanged { State: EngineState.Degraded });
}

[Fact]
public void Successful_rebuild_restores_live_and_silences_the_alarm()
{
    var (engine, factory, clock, events) = NewEngine();
    engine.Start();
    engine.DrainPending();
    var firstCableCount = factory.CableCreateCount;

    factory.LastCable!.Fault(new InvalidOperationException("device lost"));
    engine.DrainPending(); // enters Degraded, first rebuild attempt succeeds immediately

    Assert.Equal(EngineState.Live, engine.State);
    Assert.True(factory.CableCreateCount > firstCableCount); // a new cable device was built
    Assert.Null(factory.LastAlarm is { State: DeviceState.Running } ? factory.LastAlarm : null); // alarm stopped
    Assert.Contains(events, e => e is EngineEvent.RebuildResult { Role: DeviceRole.Cable, Success: true });
}
```

Add `using AdaVoice.Audio.Abstractions;` to the test usings (for `DeviceState`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "AudioEngineTests.Cable_fault"`
Expected: FAIL — `StreamFaulted` is not handled and `EnterDegraded` throws `NotImplementedException`.

- [ ] **Step 3: Implement the fault/degraded/rebuild logic**

In `Handle`, add:
```csharp
            case EngineCommand.StreamFaulted fault: HandleFault(fault.Role); break;
            case EngineCommand.WatchdogTick: HandleWatchdogTick(); break;
```

Add the handlers and replace the placeholder `EnterDegraded`:
```csharp
    private void HandleFault(DeviceRole role)
    {
        if (State == EngineState.Stopped)
            return;
        if (State != EngineState.Degraded)
        {
            _stateToRestore = State == EngineState.OffAir ? EngineState.OffAir : EngineState.Live;
            EnterDegraded(role);
        }
    }

    private void EnterDegraded(DeviceRole role)
    {
        _faultRole = role;
        _attempt = 0;
        SetState(EngineState.Degraded);
        RaiseAlarm();
        AttemptRebuild();
    }

    private void AttemptRebuild()
    {
        _attempt++;
        try
        {
            RebuildStream(_faultRole);
            Raise(new EngineEvent.RebuildResult(_faultRole, Success: true, _attempt));
            SilenceAlarm();
            _gate!.IsOpen = _stateToRestore == EngineState.Live;
            SetState(_stateToRestore);
        }
        catch (AudioDeviceException ex) when (!ex.IsTransient)
        {
            GoStopped(ex.Message);
        }
        catch (Exception)
        {
            Raise(new EngineEvent.RebuildResult(_faultRole, Success: false, _attempt));
            _nextAttemptMs = _clock.NowMs + BackoffMs[Math.Min(_attempt - 1, BackoffMs.Length - 1)];
        }
    }

    private void RebuildStream(DeviceRole role)
    {
        if (role == DeviceRole.Mic)
        {
            _passthrough!.Drift -= OnDrift;
            _mixer!.RemoveMixerInput(_passthrough.Output);
            _passthrough.Dispose();
            if (_capture is not null) _capture.StateChanged -= OnCaptureStateChanged;
            _capture?.Dispose();

            _capture = _factory.CreateCapture(DeviceRole.Mic);
            _capture.StateChanged += OnCaptureStateChanged;
            _passthrough = new MicPassthrough(_capture);
            _passthrough.Drift += OnDrift;
            _mixer.AddMixerInput(_passthrough.Output);
            _player = new PhrasePlayer(_mixer, _passthrough, _playerOptions);
            _capture.Start();
        }
        else // Cable
        {
            if (_cableRender is not null) _cableRender.StateChanged -= OnCableStateChanged;
            _cableRender?.Dispose();

            _cableRender = _factory.CreateRender(DeviceRole.Cable);
            _cableRender.StateChanged += OnCableStateChanged;
            _cableRender.Init(_gate!);
            _cableRender.Start();
        }
    }

    private void RaiseAlarm()
    {
        if (_alarmRender is not null)
            return;
        try
        {
            _alarmRender = _factory.CreateRender(DeviceRole.Alarm);
            _alarmRender.Init(new AlarmTone(AudioFormats.Engine));
            _alarmRender.Start();
        }
        catch (Exception)
        {
            // Honest limit (design §2.4): even the system default device is gone. The visual
            // DEGRADED banner already shows via StateChanged; we cannot make sound.
            _alarmRender = null;
        }
    }

    private void HandleWatchdogTick()
    {
        if (State is EngineState.Live or EngineState.OffAir)
        {
            if (_gate is not null && _clock.NowMs - _gate.LastReadMs > WatchdogStallMs)
            {
                _stateToRestore = State == EngineState.OffAir ? EngineState.OffAir : EngineState.Live;
                EnterDegraded(DeviceRole.Cable);
            }
        }
        else if (State == EngineState.Degraded && _clock.NowMs >= _nextAttemptMs)
        {
            AttemptRebuild();
        }
    }
```

Note: `EnterDegraded` is now fully implemented; delete the placeholder `private void EnterDegraded(...) => throw...;` line from Task 5.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AudioEngineTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.Audio/Engine/AudioEngine.cs tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs
git commit -m "feat(audio): AudioEngine fault -> Degraded -> rebuild with alarm"
```

---

## Task 10: Backoff retry and terminal failure → Stopped

**Files:**
- Modify: `tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`

The behavior already exists (Task 9). These tests lock in the backoff and terminal paths.

- [ ] **Step 1: Write the failing tests**

Add to `AudioEngineTests`:
```csharp
[Fact]
public void Failed_rebuild_retries_after_the_backoff_delay()
{
    var (engine, factory, clock, _) = NewEngine();
    engine.Start();
    engine.DrainPending();

    factory.FailNext(DeviceRole.Cable, transient: true); // first rebuild attempt fails
    factory.LastCable!.Fault(new InvalidOperationException("lost"));
    engine.DrainPending();
    Assert.Equal(EngineState.Degraded, engine.State); // still down, waiting on backoff

    // Too soon: a tick before the 250 ms backoff does nothing.
    clock.Advance(100);
    engine.FireWatchdog();
    engine.DrainPending();
    Assert.Equal(EngineState.Degraded, engine.State);

    // After the backoff window, the retry succeeds.
    clock.Advance(200); // now 300 ms > 250 ms
    engine.FireWatchdog();
    engine.DrainPending();
    Assert.Equal(EngineState.Live, engine.State);
}

[Fact]
public void Terminal_create_failure_stops_the_engine()
{
    var (engine, factory, _, events) = NewEngine();
    engine.Start();
    engine.DrainPending();

    factory.FailNext(DeviceRole.Cable, transient: false, message: "bad config");
    factory.LastCable!.Fault(new InvalidOperationException("lost"));
    engine.DrainPending();

    Assert.Equal(EngineState.Stopped, engine.State);
    Assert.Contains(events, e => e is EngineEvent.StateChanged { State: EngineState.Stopped, Error: "bad config" });
}
```

This needs a test helper to fire the watchdog through the engine. Add to `AudioEngine`:
```csharp
    /// <summary>Test/host hook: fire the periodic callbacks once (production uses a real timer).</summary>
    public void FireWatchdog() { /* see step 3 */ }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "AudioEngineTests.Failed_rebuild"`
Expected: FAIL — `engine.FireWatchdog` does not exist.

- [ ] **Step 3: Add the FireWatchdog test hook**

The watchdog callback (`() => Post(WatchdogTick)`) is registered with the clock. The simplest deterministic hook is to expose firing through the `ManualEngineClock`, but the engine owns the registration. Add to `AudioEngine`:
```csharp
    /// <summary>
    /// Enqueue one watchdog tick directly. The production timer (via IEngineClock) does this on
    /// its own; tests call it to drive time-based transitions deterministically.
    /// </summary>
    public void FireWatchdog() => Post(new EngineCommand.WatchdogTick());
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AudioEngineTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.Audio/Engine/AudioEngine.cs tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs
git commit -m "test(audio): cover engine backoff retry and terminal-failure stop"
```

---

## Task 11: Watchdog stall → Degraded

**Files:**
- Modify: `tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`

Behavior exists (Task 9 `HandleWatchdogTick`). This test locks in stall detection.

- [ ] **Step 1: Write the failing test**

Add to `AudioEngineTests`:
```csharp
[Fact]
public void Render_stall_beyond_500ms_goes_degraded()
{
    var (engine, factory, clock, _) = NewEngine();
    engine.Start();
    engine.DrainPending();

    // The render pulls once, stamping the gate at t=0.
    factory.LastCable!.Pull(48);

    // No further pulls. Time passes beyond the 500 ms stall threshold.
    clock.Advance(600);
    engine.FireWatchdog();
    engine.DrainPending();

    Assert.Equal(EngineState.Degraded, engine.State);
}
```

- [ ] **Step 2: Run test to verify it fails (or passes — confirm)**

Run: `dotnet test --filter "AudioEngineTests.Render_stall"`
Expected: PASS already (logic exists). If it FAILS, debug `HandleWatchdogTick` stall comparison.

> If it passes immediately, that is fine — it is a regression guard for existing behavior. The TDD value here is proving the watchdog path with a deterministic clock.

- [ ] **Step 3: Commit**

```bash
git add tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs
git commit -m "test(audio): cover watchdog render-stall -> Degraded"
```

---

## Task 12: DeviceChanged — removal and fast-path recovery

**Files:**
- Modify: `src/AdaVoice.Audio/Engine/AudioEngine.cs`
- Test: `tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `AudioEngineTests`:
```csharp
[Fact]
public void Device_removed_while_live_goes_degraded()
{
    var (engine, _, _, _) = NewEngine();
    engine.Start();
    engine.DrainPending();

    engine.Post(new EngineCommand.DeviceChanged(DeviceRole.Cable, Arrived: false));
    engine.DrainPending();

    Assert.Equal(EngineState.Degraded, engine.State);
}

[Fact]
public void Device_arrived_triggers_immediate_rebuild_during_backoff()
{
    var (engine, factory, _, _) = NewEngine();
    engine.Start();
    engine.DrainPending();

    factory.FailNext(DeviceRole.Cable, transient: true); // first attempt fails -> waiting on backoff
    factory.LastCable!.Fault(new InvalidOperationException("lost"));
    engine.DrainPending();
    Assert.Equal(EngineState.Degraded, engine.State);

    // The device comes back; recover immediately without waiting for the backoff timer.
    engine.Post(new EngineCommand.DeviceChanged(DeviceRole.Cable, Arrived: true));
    engine.DrainPending();

    Assert.Equal(EngineState.Live, engine.State);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "AudioEngineTests.Device_"`
Expected: FAIL — `DeviceChanged` is not handled.

- [ ] **Step 3: Add the DeviceChanged case**

In `Handle`, add:
```csharp
            case EngineCommand.DeviceChanged dc: HandleDeviceChanged(dc.Role, dc.Arrived); break;
```

Add the handler:
```csharp
    private void HandleDeviceChanged(DeviceRole role, bool arrived)
    {
        if (arrived)
        {
            // Fast path: a device we are waiting on came back — retry now, skip the backoff wait.
            if (State == EngineState.Degraded && role == _faultRole)
                AttemptRebuild();
            return;
        }

        // Removed: if it is a device we are using and we are running, treat it as a fault.
        if (State is EngineState.Live or EngineState.OffAir && role is DeviceRole.Mic or DeviceRole.Cable)
        {
            _stateToRestore = State == EngineState.OffAir ? EngineState.OffAir : EngineState.Live;
            EnterDegraded(role);
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter AudioEngineTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.Audio/Engine/AudioEngine.cs tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs
git commit -m "feat(audio): AudioEngine DeviceChanged removal + fast-path recovery"
```

---

## Task 13: Drift events forwarded out

**Files:**
- Modify: `tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs`

Behavior exists (`OnDrift` wired in `BuildGraph`). This test proves drift surfaces as an engine event.

- [ ] **Step 1: Write the failing test**

Add to `AudioEngineTests`:
```csharp
[Fact]
public void Mic_drift_is_forwarded_as_an_engine_event()
{
    var (engine, factory, _, events) = NewEngine();
    engine.Start();
    engine.DrainPending();

    // Cause an overrun: push far more than the 100 ms backlog limit without anyone pulling.
    for (var i = 0; i < 4; i++)
        factory.LastMic!.Push(Enumerable.Repeat(0.1f, 48_000 / 10).ToArray()); // ~100 ms each

    Assert.Contains(events, e => e is EngineEvent.DriftLogged);
}
```

- [ ] **Step 2: Run test to verify it fails or passes — confirm**

Run: `dotnet test --filter "AudioEngineTests.Mic_drift"`
Expected: PASS (wiring exists). If FAIL, verify `OnDrift` is subscribed in `BuildGraph` and re-subscribed in `RebuildStream`.

- [ ] **Step 3: Commit**

```bash
git add tests/AdaVoice.Audio.Tests/Engine/AudioEngineTests.cs
git commit -m "test(audio): cover engine drift-event forwarding"
```

---

## Task 14: EngineRunner (the production control thread)

**Files:**
- Create: `src/AdaVoice.Audio/Engine/EngineRunner.cs`
- Test: `tests/AdaVoice.Audio.Tests/Engine/EngineRunnerTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/AdaVoice.Audio.Tests/Engine/EngineRunnerTests.cs`:
```csharp
using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Tests.Engine.Fakes;

namespace AdaVoice.Audio.Tests.Engine;

public class EngineRunnerTests
{
    [Fact]
    public void Runner_processes_commands_on_its_own_thread()
    {
        var engine = new AudioEngine(new FakeDeviceFactory(), new ManualEngineClock());
        var reached = new ManualResetEventSlim(false);
        engine.Events += (_, e) =>
        {
            if (e is EngineEvent.StateChanged { State: EngineState.Live })
                reached.Set();
        };

        using var runner = new EngineRunner(engine);
        runner.Start();
        engine.Start(); // posted to the queue; the runner thread processes it

        Assert.True(reached.Wait(TimeSpan.FromSeconds(2)), "engine did not reach Live");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter EngineRunnerTests`
Expected: FAIL — `EngineRunner` does not exist.

- [ ] **Step 3: Implement EngineRunner**

`src/AdaVoice.Audio/Engine/EngineRunner.cs`:
```csharp
namespace AdaVoice.Audio.Engine;

/// <summary>
/// Runs an <see cref="AudioEngine"/>'s command loop on one dedicated background thread. This is
/// the production driver; tests usually call <see cref="AudioEngine.DrainPending"/> directly.
/// </summary>
public sealed class EngineRunner(AudioEngine engine) : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    public void Start()
    {
        if (_thread is not null)
            return;
        _thread = new Thread(Loop) { Name = "AudioEngine", IsBackground = true };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_cts.IsCancellationRequested)
            engine.TryProcessNext(timeoutMs: 100);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter EngineRunnerTests`
Expected: PASS.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test AdaVoice.slnx -c Release`
Expected: all green (existing 24 + the new engine tests).

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.Audio/Engine/EngineRunner.cs tests/AdaVoice.Audio.Tests/Engine/EngineRunnerTests.cs
git commit -m "feat(audio): add EngineRunner control thread"
```

---

## Task 15: WasapiDeviceFactory (build-only hardware adapter)

Lets the engine run on real hardware. No unit test (hardware adapter, like the existing `WasapiCaptureDevice`); verified by build and, later, the `AudioSeamCheck` runner.

**Files:**
- Create: `src/AdaVoice.Audio.Wasapi/WasapiDeviceFactory.cs`

- [ ] **Step 1: Implement the factory**

`src/AdaVoice.Audio.Wasapi/WasapiDeviceFactory.cs`:
```csharp
using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Engine;
using NAudio.CoreAudioApi;

namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Real device factory: resolves WASAPI endpoints and builds the production device seams. Uses
/// sensible defaults for now (default comms mic, "CABLE Input", default render for the alarm);
/// device selection from saved settings is a later slice. Throws
/// <see cref="AudioDeviceException"/> so the engine can tell transient from terminal failures.
/// </summary>
public sealed class WasapiDeviceFactory : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();

    public IAudioCaptureDevice CreateCapture(DeviceRole role)
    {
        if (role != DeviceRole.Mic)
            throw new AudioDeviceException($"Capture not supported for role {role}.", isTransient: false);

        var mic = WasapiDevices.DefaultCommunicationsMic()
            ?? throw new AudioDeviceException("No default communications microphone found.", isTransient: true);
        return new WasapiCaptureDevice(mic);
    }

    public IAudioRenderDevice CreateRender(DeviceRole role)
    {
        var device = role switch
        {
            DeviceRole.Cable => WasapiDevices.FindByName(DataFlow.Render, "CABLE Input")
                ?? throw new AudioDeviceException("CABLE Input not found. Is VB-CABLE installed?", isTransient: true),
            DeviceRole.Alarm => DefaultRenderOrThrow(),
            _ => throw new AudioDeviceException($"Render not supported for role {role}.", isTransient: false),
        };
        return new WasapiRenderDevice(device);
    }

    private MMDevice DefaultRenderOrThrow()
    {
        try
        {
            return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (Exception ex)
        {
            throw new AudioDeviceException("No default output device for the alarm.", isTransient: true);
        }
    }

    public void Dispose() => _enumerator.Dispose();
}
```

> Note: this class intentionally does **not** yet implement `IAudioDeviceFactory` directly if the interface method shapes need a small adapter — it already matches the interface (`CreateCapture`/`CreateRender`). Add `: IAudioDeviceFactory` to the class declaration: `public sealed class WasapiDeviceFactory : IAudioDeviceFactory, IDisposable`. Verify `WasapiDevices.FindByName` and `DefaultCommunicationsMic` exist (they do — used by `tools/AudioSeamCheck`).

- [ ] **Step 2: Add the interface to the declaration**

Change the class line to:
```csharp
public sealed class WasapiDeviceFactory : IAudioDeviceFactory, IDisposable
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/AdaVoice.Audio.Wasapi/AdaVoice.Audio.Wasapi.csproj -c Release`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Run the full suite once more**

Run: `dotnet test AdaVoice.slnx -c Release`
Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.Audio.Wasapi/WasapiDeviceFactory.cs
git commit -m "feat(audio): add WasapiDeviceFactory (real device adapter)"
```

---

## Deferred to a follow-up plan (Windows adapters, hardware-validated)

These are intentionally **not** in this plan because they cannot be unit-tested and need hardware (design 08 §4):

- **`DeviceMonitor`** (`IMMNotificationClient`) — translates OS device add/remove/default-change events into `EngineCommand.DeviceChanged`. The engine's *handling* of those commands is fully tested here (Task 12); only the COM producer is deferred.
- **Host wiring + `RegisterApplicationRestart`** — the composition root that builds `WasapiDeviceFactory`, a real `IEngineClock` (`Stopwatch` + `System.Threading.Timer`), the `EngineRunner`, and the `DeviceMonitor`, and registers OS restart.
- **Extend `tools/AudioSeamCheck`** to drive the full engine on hardware (8-hour soak, device-yank, alarm-on-system-default checks).

---

## Self-review

**Spec coverage:** state machine (Tasks 5–12 ✓), cable + alarm streams (Tasks 5, 9 ✓), OFF AIR gate (Tasks 2, 8 ✓), watchdog (Tasks 9, 11 ✓), targeted rebuild + backoff (Tasks 9, 10 ✓), DeviceChanged handling (Task 12 ✓), independent alarm + honest limit (Task 9 ✓), drift forwarding (Task 13 ✓), control thread (Task 14 ✓), device factory boundary (Task 15 ✓). Deferred items (DeviceMonitor COM producer, host `RegisterApplicationRestart`) are called out explicitly and match the spec's "out of scope / later" notes.

**Placeholders:** Task 5 declares `EnterDegraded`/`GoStopped` as throwing placeholders; both are replaced with full bodies (Task 6 `GoStopped`, Task 9 `EnterDegraded`) before any test reaches them, and the plan says so at each point. No "TODO"/"handle edge cases"/"similar to" remain.

**Type consistency:** method names used across tasks are consistent — `DrainPending`, `TryProcessNext`, `Post`, `FireWatchdog`, `BuildGraph`, `RebuildStream`, `EnterDegraded`, `AttemptRebuild`, `RaiseAlarm`, `SilenceAlarm`, `GoStopped`, `TearDown`, `SetState`, `Raise`, `OnDrift`, `OnCaptureStateChanged`, `OnCableStateChanged`. Properties: `State`, `ActivePhraseId`, `Events`. Fakes: `ControllableCaptureDevice.Push/Fault`, `ControllableRenderDevice.Pull/Fault/Captured`, `FakeDeviceFactory.LastMic/LastCable/LastAlarm/CableCreateCount/FailNext`, `ManualEngineClock.NowMs/Advance/FireTicks`. `MixingSampleProvider.RemoveMixerInput` is a real NAudio method (matches `AddMixerInput` used elsewhere).
