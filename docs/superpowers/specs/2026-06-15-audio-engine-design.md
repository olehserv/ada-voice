# AudioEngine — Design (Phase 1, engine core + reliability)

_Date: 2026-06-15. Status: approved design, not yet implemented._

## 1. Problem

We have working audio **parts** — `MicPassthrough`, `PhrasePlayer`, the DSP primitives, and
the WASAPI device seams — but nothing **owns their lifecycle**. Phase 1 needs the orchestrator
that the whole product reliability story rests on: it starts and stops the audio graph, plays
phrases, goes OFF AIR, and — most importantly — **never lets the operator's microphone stop
silently**. If the mic path dies mid-call, the client hears silence; that failure must always
be loud (visible and audible).

This document designs that orchestrator: the `AudioEngine`.

### Scope

**In scope (this slice):**
- The engine state machine: `Stopped / Live / OffAir / Degraded` (the `EngineState` enum exists).
- Lifecycle: open/close/rebuild the **capture** stream and the **cable** render stream.
- Composition of the existing parts into one running graph.
- Watchdog (render-pull stall detection).
- `DeviceMonitor` integration (device add/remove/default-change → targeted rebuild).
- The **DEGRADED alarm** on the system default output device.
- Drift logging (forward `MicPassthrough.Drift` out as an event).
- The boundary for `RegisterApplicationRestart` (placed in the host, not the core).

**Out of scope (separate later docs):**
- The **Recorder** (record / trim / RMS loudness-match / preview / save). OFF AIR is designed
  here as a *state the engine supports*, but the Recorder that drives it comes later.
- The optional **headphone monitor** render stream.
- The WPF UI / ViewModel layer.

## 2. Proposed design

Single process, single control thread, ports-and-adapters. The engine lives in the
`net10.0` core (`AdaVoice.Audio`) and never references WASAPI or COM. It is driven entirely by
**immutable command messages on one queue**, processed by **one control loop** — so all state
transitions and all stream open/close happen on a single thread, with no locks in the core
logic and no races between the four sources that can trigger a rebuild (user, stream error,
device monitor, watchdog).

### 2.1 Components

```
AdaVoice.Audio (net10.0 — no Windows)
└── Engine/
    ├── AudioEngine          orchestrator: owns the control loop, state, and streams
    ├── EngineState (exists)  Stopped / Live / OffAir / Degraded
    ├── EngineCommand         immutable messages IN  (Start, Stop, Play, StopPhrase,
    │                         EnterOffAir, ExitOffAir, DeviceChanged, StreamFaulted, WatchdogTick)
    ├── EngineEvent           immutable notifications OUT (StateChanged, DriftLogged, RebuildResult)
    ├── IAudioDeviceFactory   NEW seam: creates capture/render devices by role
    ├── IEngineClock          NEW seam: control-loop thread + watchdog ticks (pumpable in tests)
    ├── CableGate             NEW: silence gate between mixer and cable; also stamps last-read time
    └── (composes existing)   MicPassthrough · PhrasePlayer · MixingSampleProvider
```

#### New seams and why they exist

- **`IAudioDeviceFactory`** — the engine must *re-create* devices on rebuild, so it cannot be
  handed fixed device instances; and it must stay Windows-free. So it depends on a factory:
  roughly `IAudioCaptureDevice CreateCapture(DeviceRole role)` and
  `IAudioRenderDevice CreateRender(DeviceRole role)`, where `DeviceRole` is `Mic`, `Cable`, or
  `Alarm` (alarm = system default output). `AdaVoice.Audio.Wasapi` implements the real factory
  (resolves MMDevice IDs, builds `WasapiCaptureDevice` / `WasapiRenderDevice`). Tests implement
  a fake that returns `FileCaptureDevice` / `MemoryRenderDevice` and can be told to fail a
  create or fault a device on command.

- **`IEngineClock`** — hides both "give me the control-loop thread" and "tick the watchdog
  every N ms". In production it is a real thread + timer; in tests it is a `ManualEngineClock`
  the test drives step by step, so transitions and watchdog timeouts are deterministic with no
  `Thread.Sleep`.

- **`CableGate`** — a thin `ISampleProvider` between the mixer and the cable render. OFF AIR
  sets it to output silence (the stream stays open and keeps pulling). It also stamps the last
  read time on every `Read`, which the watchdog uses to detect a stalled render.

#### Dependency direction (inward only)

```
Wasapi factory ─┐
ViewModel (later)├─→ AudioEngine ─→ IAudioCaptureDevice / IAudioRenderDevice (seams)
DeviceMonitor ──┘                └─→ MicPassthrough / PhrasePlayer (existing parts)
```

The engine depends only on interfaces and the pure audio parts. This keeps `AdaVoice.Audio`
compiling for `net10.0` and fully testable with fakes.

#### Public API (boundary rule)

The engine exposes a small thread-safe API — `Start`, `Stop`, `Play(phrase)`, `StopPhrase`,
`EnterOffAir`, `ExitOffAir`. Each call only **enqueues** a command and returns immediately.
Callers never block on audio work. State changes come back as `StateChanged` events.

### 2.2 State table

Every transition is processed on the single control loop, so there are no concurrent
transitions.

| From | Trigger (command / event) | Action | To |
|---|---|---|---|
| Stopped | `Start` | Open capture + cable render via factory; build passthrough→mixer→gate→cable; start. | Live |
| Stopped | `Start` fails to open | Raise alarm; begin rebuild with backoff. | Degraded |
| Live | `Play(phrase)` | `PhrasePlayer.Play` (ducks mic). | Live |
| Live | `StopPhrase` | `PhrasePlayer.Stop` (fade-out, un-duck). | Live |
| Live | `EnterOffAir` | Set the cable gate to silence (stream stays open). | OffAir |
| OffAir | `ExitOffAir` | Open the cable gate again. | Live |
| OffAir | `Play` / `StopPhrase` | Ignored + logged (no phrase reaches a paused cable in this slice). | OffAir |
| Live / OffAir | `StreamFaulted` (capture or cable errored) | Raise alarm on system default; begin rebuild; remember the state to return to. | Degraded |
| Live / OffAir | `DeviceChanged` (affected device removed / default changed) | Rebuild only the affected stream; if it fails → fault path. | Degraded (if rebuild fails) / unchanged |
| Live / OffAir | `WatchdogTick` (render pull stalled > 500 ms) | Treat as a fault. | Degraded |
| Degraded | `RebuildSucceeded` | Silence alarm; restore the remembered state. | Live or OffAir |
| Degraded | rebuild failed, attempts remain | Schedule next attempt (backoff). | Degraded |
| Degraded | retries exhausted (terminal error) **or** `Stop` | Stop streams; surface a terminal, loud error. | Stopped |
| Live / OffAir / Degraded | `Stop` | Stop all streams; silence alarm. | Stopped |

**Decision — OFF AIR is a silence gate, not a stream teardown.** The `CableGate` flips to
silence; the cable stream keeps pulling so the watchdog stays valid and on/off is instant.
Tearing the stream down would re-trigger the watchdog and re-apply the ducking opt-out each
time — the gate avoids both.

**Decision — Degraded retry policy.** Exponential backoff: 250 ms → 500 ms → 1 s → 2 s → 5 s,
then steady 5 s polling while a device is still absent. A `DeviceChanged` "device arrived"
event triggers an **immediate** rebuild attempt regardless of backoff, so replugging the
headset recovers fast. "Retries exhausted → Stopped" applies only to a **terminal** factory
error (a non-transient/config failure), never to a device that is simply still absent.

### 2.3 Data flow

Two independent paths that never block each other.

**Path 1 — control (slow, serialized, no audio work):**

```
caller / DeviceMonitor / watchdog / stream-error callback
      │  (each posts an immutable message)
      ▼
  command queue ──→ control loop (one thread) ──→ executes the transition
                                              └──→ publishes EngineEvent out
```

`engine.Play(phrase)` enqueues a `Play` command and returns. The loop dequeues it and calls
`PhrasePlayer.Play`, which wires one provider into the mixer. The caller never waits.

**Path 2 — audio (fast, driver-paced):**

```
capture callback → MicPassthrough → ┐
                                    ├─→ Mixer → CableGate → cable render → CABLE Input
PhrasePlayer phrase providers ──────┘                 (gate = silence when OFF AIR)

alarm tone provider ─────────────────────────────────→ alarm render → system default (Degraded only)
```

This pulls at the device's pace. The control loop only **wires/unwires** providers (add/remove
a mixer input, flip the gate) — operations the existing parts already make thread-safe
(`MixingSampleProvider` locks internally; `PhrasePlayer` handles lock order). The loop never
copies audio buffers.

**Out-of-band signals feeding back into Path 1:**

- **Stream error:** a device's `StateChanged → Faulted` callback enqueues `StreamFaulted`.
- **Watchdog:** the `CableGate` stamps last-read time on every `Read`; `IEngineClock` posts a
  `WatchdogTick` periodically; the loop compares the stamp's age to 500 ms.
- **Drift:** `MicPassthrough.Drift` fires → the loop forwards it as a `DriftLogged` event.

**Events out:** the engine raises `StateChanged` / `DriftLogged` / `RebuildResult` on the
control thread and stops there. It does **not** know about the WPF dispatcher — the future
ViewModel subscribes and marshals to the UI thread itself. Logging is the same: the engine
emits `DriftLogged`; the app layer writes it to Serilog. **The core stays logging-free.**

### 2.4 Failure handling & the "never silently dead" guarantee

**Targeted rebuild sequence:**

```
fault detected (StreamFaulted | DeviceChanged | WatchdogTick)
  → enter Degraded, remember the state to return to (Live or OffAir)
  → raise the alarm
  → identify the affected stream (capture OR cable)
  → dispose the dead device; ask the factory for a fresh one
  → re-wire it into the graph (capture → passthrough, or mixer → gate → cable)
  → start it
      success → RebuildSucceeded → silence alarm → restore remembered state
      failure → schedule next attempt with backoff, stay Degraded
```

Only the broken stream is rebuilt; the healthy one keeps running. A brief cable silence during
the rebuild is the honest Degraded reality — audible and visible, not hidden.

**The alarm — the audible half of the cardinal rule:**

- The alarm render is its **own** device, resolved fresh from the factory as the system default
  output when entering Degraded. It is deliberately independent of the cable (and future
  monitor) streams, so a dead cable cannot take the alarm down with it.
- Enter Degraded → start the alarm tone; leave Degraded (recovered, or Stopped by the user) →
  stop it.
- **Honest limit:** if even the system-default device is gone, we cannot make sound. We still
  raise `StateChanged(Degraded)` so the visual banner shows, and emit a log event. Documented,
  not pretended away.

**Boundary placements:**

- **`RegisterApplicationRestart`** is a process-level Win32 call. It belongs in the **Windows
  host / composition root**, not inside the `net10.0` engine. The engine's role after a
  relaunch is just the normal Stopped→Live path on `Start`.
- **`DeviceMonitor`** (`IMMNotificationClient`) lives in the Wasapi layer and feeds
  `DeviceChanged` messages into the queue. The engine depends only on the interface.

### 2.5 Testing

Every transition is a deterministic, fast unit test driven by the two new seams — no real
hardware, no `Thread.Sleep`, no flake.

- **`FakeDeviceFactory`** returns the existing `FileCaptureDevice` / `MemoryRenderDevice` fakes
  and can be told to fail a create, throw a terminal error, or fault a device after N reads.
- **`ManualEngineClock`** lets the test drain the command queue synchronously and advance time
  to fire watchdog ticks. A test reads: post command → drain → assert state.

Coverage (maps to design 08 §3):

| Test | How it's driven |
|---|---|
| Stopped → Live | `Start`, drain, assert Live + capture/cable started |
| Live → OffAir → Live | assert no samples reach the cable while OFF AIR; resume after |
| Live → Degraded (stream fault) | fake device faults → Degraded + alarm render created on system default |
| Live → Degraded (watchdog) | stop draining cable reads, advance clock > 500 ms, tick → Degraded |
| Degraded → Live (rebuild) | clear fault, advance to backoff time → RebuildSucceeded + alarm stopped + Live |
| Degraded → OffAir (restore) | fault while OffAir, recover → returns to OffAir, not Live |
| Degraded → Stopped (terminal) | factory throws terminal error → Stopped + terminal error event |
| Degraded → Stopped (user) | `Stop` while Degraded → Stopped + alarm silenced |
| Device-arrived fast path | `DeviceChanged(arrived)` during backoff → immediate rebuild attempt |
| Drift forwarded | raise `MicPassthrough.Drift` → a `DriftLogged` event comes out |
| Play ignored while OffAir | `Play` during OffAir → ignored + logged |

**Not unit-tested** (manual/hardware checklist, design 08 §4): real WASAPI timing, real
device-removal COM events, the 8-hour soak. The seams stop at the hardware edge — the same line
the current tests hold.

## 3. Why this design

- **Single control thread + queue** removes the hardest bug class (concurrent rebuilds from
  four sources) by construction, and matches the already-reviewed design 03 §4.
- **The two new seams** (`IAudioDeviceFactory`, `IEngineClock`) keep the engine in the pure
  `net10.0` core and make every failure path a deterministic test instead of a flaky one.
- **The cable gate** gives instant, watchdog-safe OFF AIR without stream teardown.
- **The independent alarm device** is what actually delivers the "never silently dead"
  guarantee, even when the cable is dead.

## 4. Boundaries (responsibility per part)

- **`AudioEngine`** — owns state, the queue, the loop, and the live streams. The only code that
  opens/closes streams or changes state.
- **`IAudioDeviceFactory` impl (Wasapi)** — resolves devices and builds real WASAPI devices.
- **`DeviceMonitor` (Wasapi)** — translates OS device events into `DeviceChanged` messages.
- **`CableGate`** — OFF AIR silence + watchdog liveness stamp.
- **Host / composition root (Windows)** — calls `RegisterApplicationRestart`; wires the real
  factory, monitor, and clock into the engine; subscribes to events and logs / updates UI.
- **Existing parts** (`MicPassthrough`, `PhrasePlayer`, DSP) — unchanged; the engine composes
  them.

## 5. Dependencies

- The engine depends on: the device seams, `IAudioDeviceFactory`, `IEngineClock`, and the
  existing audio parts — all in or referenced from `AdaVoice.Audio` (`net10.0`).
- `AdaVoice.Audio.Wasapi` depends on the core and implements the factory + monitor.
- The future UI/host depends on the engine; the engine depends on nothing upward.

## 6. Alternatives considered

- **Lock-based synchronized methods** (no queue): simpler to start, but slow WASAPI rebuilds
  run under a lock on arbitrary threads, and COM device callbacks can fire during a rebuild →
  reentrancy/ordering bugs. Rejected for a livelihood-critical path.
- **Hybrid** (lock for state + a single-threaded rebuild scheduler): effectively Approach A
  with the queue half-built. Rejected in favour of doing A cleanly.
- **OFF AIR by stopping the cable stream**: re-triggers the watchdog and re-applies the ducking
  opt-out each time. Rejected in favour of the silence gate.

## 7. Trade-offs

- **Gain:** single-threaded state logic, deterministic tests, a real "never silently dead"
  guarantee, a pure core.
- **Cost:** more upfront machinery (message types, the loop, two seams) than a lock-based
  version. Accepted — it is the right backbone for the most reliability-critical component.

## 8. Risks

- **Alarm device also unavailable** — handled honestly (visual + log only); documented limit.
- **Backoff vs. give-up tuning** — concrete numbers above are defaults; the 8-hour soak and
  real device-yank tests in Phase 1 may refine them.
- **Rebuild glitch length** — a brief cable silence during rebuild is expected; its real
  duration is a hardware measurement, not a design guarantee.

## 9. Recommendation

Build this slice now: the state machine, the two seams, the cable gate, the watchdog, the
`DeviceMonitor` integration, and the independent alarm — all test-first against fakes. Defer the
Recorder and the headphone monitor to their own slices. `RegisterApplicationRestart` is a
one-line host call added when the host project exists.
