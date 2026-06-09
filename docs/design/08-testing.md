# 08 — Test Strategy

Added by eng review 2026-06-10 (Issue 7): the audio engine is reliability-critical (the
operator's livelihood runs through it 8 hours a day), so testability is designed in now —
retrofitting fake-device seams into NAudio code later is painful.

## 1. Testability architecture: device seams

The audio core never touches NAudio device classes directly. Two interfaces isolate all
hardware:

```
IAudioCaptureDevice          IAudioRenderDevice
  Start()/Stop()               Start()/Stop()
  event DataAvailable(buf)     int Read(float[] buf)   // pull-based, mirrors WASAPI callback
  Format                       Format
  event StateChanged           event StateChanged
```

- **Production implementations** wrap `WasapiCapture` / `WasapiOut` (+ the ducking opt-out
  interop).
- **Test implementations**: `FileCaptureDevice` (feeds a WAV as if spoken into the mic, at
  controllable pace), `MemoryRenderDevice` (collects rendered output for assertions),
  `FaultyDevice` decorators (throw / disappear / stall on command to drive failure paths).

`DeviceMonitor` is similarly behind `IDeviceMonitor` so device-removal events can be fired
synthetically in tests.

## 2. Test layers

| Layer | Framework | What it covers |
|---|---|---|
| Unit (audio core) | xUnit | State machine, mixer behavior, ducking ramps, single-playback rule, OFF AIR transitions, watchdog, drift policy — all against fake devices |
| Unit (DSP, golden files) | xUnit + reference WAVs | Trim, loudness match, fade-out: feed known input WAV → compare output against stored reference (sample-accurate within tolerance) |
| Unit (storage) | xUnit | Repository atomic writes, corrupt-file handling, orphaning, import merge/replace |
| Unit (services) | xUnit | HotkeyService registration/conflict surfacing, LocalizationService completeness |
| Integration (manual + scripted) | checklist | Real devices, real VB-CABLE, real Zoho call — the things only hardware can verify |

## 3. Required test coverage by component

### AudioEngine / state machine
- Every transition in the 06 §2 diagram: Stopped→Live, Live→OffAir→Live, Live→Degraded
  (device loss via `FaultyDevice`), Degraded→Live (rebuild succeeds), Degraded→Stopped
  (retries exhausted), OffAir→Degraded.
- Watchdog: render pull stalls > 500 ms ⇒ rebuild triggered exactly once.
- Ducking opt-out invoked on every stream (re)start (assert via test double).
- Drift policy: overrun ⇒ oldest samples dropped + logged; underrun ⇒ silence inserted +
  logged (construct with mismatched fake clocks).
- DEGRADED alarm raised through the alarm channel even when `monitorEnabled=false`.

### Mixer / PhrasePlayer
- Single-playback: trigger during playback replaces (default) or is ignored (toggle) —
  assert sample-level output via `MemoryRenderDevice`.
- Stop applies a 10 ms linear fade (golden file: no discontinuity > threshold).
- Duck ramp: mic branch reaches `micDuckDb` within `duckRampMs` ±1 buffer and returns after.
- Mono→stereo upmix correctness at the cable edge.

### Recorder (DSP golden files)
- Trim: leading/trailing silence removed, 150 ms padding kept (reference WAVs).
- Loudness match: output RMS within ±0.5 dB of `micReferenceRms` target; peak never exceeds
  −3 dBFS (test with quiet, loud, and clipped takes).
- OFF AIR: opening the recorder pauses the cable branch (assert no samples reach the cable
  device while open), state restored on close.
- Disk-full: writer failure mid-take aborts cleanly, temp file removed, library untouched.

### Storage / repository
- kill -9 simulation: interrupt between tmp-write and rename ⇒ original intact (already a
  Phase 2 exit criterion; automated, not manual).
- Corrupt `library.json` ⇒ startup enters recovery path (load newest backup, surface message)
  — never crash, never silently start empty.
- Missing/corrupt WAV ⇒ phrase flagged broken; play refused gracefully.
- Delete ⇒ metadata gone, file renamed `deleted-{id}.wav`, daily backup still includes it.
- Export→import round-trip: lossless for active phrases; orphans excluded from export.

### Services
- HotkeyService: registration failure surfaces a typed error (assert with a pre-registered
  conflicting hotkey); re-registration after reassignment.
- Localization completeness: a test enumerates every resource key and asserts presence in
  `uk`, `pl`, and `en` — a missing translation fails CI, not the operator.

## 4. Manual call-test checklist (only hardware can verify these)

Run on the target machine against a real Zoho Voice call (Phase 0 initially; repeated at the
post-Phase-3 pilot and before v1):

- [ ] Far end hears phrase clearly; intelligibility unaffected by Chrome NS/EC/AGC
- [ ] Far end hears live voice between phrases; no level jump at phrase boundaries
- [ ] Ducked mic actually sounds ducked to the far end (post-AGC check)
- [ ] `Pause` stop hotkey fires while Chrome is focused; playback stops within a blink
- [ ] Mouth-to-Chrome latency measured and recorded (loopback recording method)
- [ ] Communications ducking: start/stop a call repeatedly; cable stream level stays constant
- [ ] Mid-call fallback rehearsal: switch Chrome mic to hardware headset; call continues
- [ ] Device yank: unplug headset mid-call ⇒ DEGRADED alarm audible on system default device
- [ ] Kill the app process mid-call ⇒ Windows relaunches it; recovery toast appears
- [ ] 8-hour soak: drift events logged < a few per hour; RSS flat

## 5. Phase gates (test-related exit criteria)

Phase exit criteria in the [roadmap](../roadmaps/mvp-roadmap.md) reference this document;
the rule is: **a phase's code ships with its tests** — coverage is written alongside the
feature, not deferred. CI runs the unit + golden-file suites on every commit from Phase 1 on.
