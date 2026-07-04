# Full Codebase Review — 2026-07-04

Scope: `src/`, `tests/`, build props, docs cross-checks. Method: 6 parallel specialist review passes
(audio engine/DSP, WASAPI interop, Core storage/security, App UI, Host, tests) plus an independent
verification pass over ~2,700 lines of the most critical code. Every Critical/High finding below was
verified directly against the source, not just reported by one pass.

---

# Executive Summary

| Dimension | Score | One-line reason |
|---|---|---|
| Architecture | **8/10** | Clean layering, real seams, single-writer command queue. Deductions: EngineHost is 5 interfaces in one class; sync/async discipline is per-method, not per-contract. |
| Security | **7/10** | Local-first, no network, zip-slip genuinely mitigated, safe deserialization. Missing: zip resource limits, import can overwrite audio, no filename hygiene beyond flattening. |
| Reliability | **5/10** | The recovery paths — the code that exists for bad days — hold most of the real bugs: ducking lost after mic rebuild, 100 ms rebuild storms, crash = dead app with no log and no restart. |
| Maintainability | **8/10** | Small files, honest comments that match the code (rare), consistent style. Minor duplication. |
| Test Quality | **7/10** | 302 deterministic, well-designed tests — but 0 tests on the WASAPI layer, 0 on EngineHost, and fakes that can't see the device-lifecycle bug class. |

## Top 10 Risks

1. **Mic ducking is permanently lost after a mic device recovery** — phrases then play over the full-volume live mic for the rest of the session (C1).
2. **A transiently locked `library.json` + one edit = the whole library is overwritten** with a seeded default (C2).
3. **Any unexpected exception crashes the app mid-call, unlogged, and it stays dead** — no global handlers, no `RegisterApplicationRestart` in the WPF app (H2, H3).
4. **Any non-`AudioDeviceException` during a Degraded rebuild turns backoff into a 10 Hz device-churn loop** forever (H1).
5. **Previewing a pending take freezes the whole UI — including STOP and the global hotkey — for the take's full length** (H5).
6. **A failed engine Start is invisible**: the error string is dropped at the host seam, so "VB-Cable missing" looks like "button does nothing" (H7).
7. **Library import can silently overwrite existing WAV recordings** — the one place the "audio is never destroyed" invariant breaks (H9).
8. **The ducking-opt-out COM interop has wrong native signatures** (`[PreserveSig]` missing) — works on x64 by accident; its error checks have never worked (H8).
9. **Two app instances double the mic into the call** and silently lose logs/settings — no single-instance guard (H4).
10. **Drift events run file logging on the audio capture/render threads** (under the mixer lock on the underrun path), exactly when audio timing is already bad (H10).

---

# Architecture Overview

```
AdaVoice.App (WPF, MVVM)          — Views + 17 VMs; talks only to host seams; dialogs injected as delegates
   │ depends on
AdaVoice.Host                     — EngineHost: composition root, implements IPlaybackHost/IRecorderHost/
   │                                ILibraryHost/ISettingsHost/ISetupHost; runs the engine control thread
   ├── AdaVoice.Audio             — engine state machine (command queue), passthrough, player, recorder,
   │      │                         DSP, WAV I/O; no Windows dependency; fully fake-testable
   │      └── AdaVoice.Audio.Wasapi — NAudio/COM seam: devices, monitor, ducking opt-out
   └── AdaVoice.Core              — domain + JSON storage, backups, import/export; no audio dependency
```

**Dependency direction is clean** (UI → Host → Audio/Core → Wasapi) and enforced by project references.
**Threading model** (documented and mostly honored): one engine control thread consumes a
`BlockingCollection` command queue; public engine API only enqueues; WASAPI callbacks and the clock
only `Post`; events fire on the control thread (with one documented render-thread exception) and the
UI marshals via an injected `Dispatcher.BeginInvoke`. The model's true weak points: a polling
`WaitForState` bridge (no completion signal on the queue), and two contract violations found below
(H10 drift events, H1 exception types).

**Critical flows traced:** play (tile → `PlayEntry` → WAV load on UI thread → queue → mixer),
record (OFF AIR gate → second capture → trim/loudness → WAV-first catalogue), degrade/rebuild
(fault → alarm + backoff → targeted rebuild → state restore), storage (atomic tmp+rename, quarantine,
backup recovery), import/export (zip, flattened names).

---

# Findings

## Critical

### [Critical] C1 — Mic ducking permanently broken after a mic rebuild
- Confidence: High (verified)
- Location: `src/AdaVoice.Audio/Engine/AudioEngine.cs` `RebuildMic()` (268–277), `BuildGraph()` (367); `src/AdaVoice.Audio/Playback/PhrasePlayer.cs` (38, 48)
- Problem: `PhrasePlayer` stores the passthrough in `private readonly IMicDuck _mic`. `RebuildMic()` creates a **new** `MicPassthrough` and puts it in the mixer, but never re-points the player. All later `Duck()` calls hit the old, disposed passthrough's `RampGain`, which is no longer in the graph.
- Impact: After the exact scenario the rebuild exists for (headset unplug/replug), every phrase plays with the live mic at full volume underneath it, silently, until app restart. Core feature loss in the primary recovery path.
- Evidence: `RebuildMic` sets `_passthrough = new MicPassthrough(_capture)` and `_mixer!.AddMixerInput(...)` — no `_player` update; `PhrasePlayer._mic` is `readonly`.
- Recommendation: Add `PhrasePlayer.ReplaceMic(IMicDuck)` (or a `MicDuckRelay : IMicDuck` owned by the engine that forwards to the current passthrough) and call it at the end of `RebuildMic()`. Add the missing test: fault mic → rebuild → play → assert `CurrentMicGain < 1`.

### [Critical] C2 — `ReadError` load: the first edit overwrites the real library with the seeded stand-in
- Confidence: High (verified)
- Location: `src/AdaVoice.Core/PhraseLibraryService.cs` — gate exists only at line 63; mutators save unconditionally at 114, 134, 155, 169, 190, 241; `src/AdaVoice.Core/Storage/JsonPhraseRepository.cs` 28–33
- Problem: A transiently locked `library.json` (AV scan, backup tool) loads a **seeded default** with `LoadStatus.ReadError`. The comment says this stand-in exists "specifically so it is never overwritten" — but only the tag-migration save is gated. Every mutator (`Add`, `Delete`, `AddCategory`, …) calls `_repository.Save(_library)` regardless. The operator sees an empty board, adds anything, and the real library is replaced.
- Impact: Full metadata loss from a transient file lock. Compounding: no UI surfaces `LoadStatus` at all (zero references in `AdaVoice.App`; `EngineHost` only logs it), so the empty board comes with no explanation.
- Recommendation: Make mutators refuse (or no-op with an error) while `LoadStatus == ReadError` until a successful `Reload()`. Separately, show a dialog for `ReadError`/`Corrupt`/`RecoveredFromBackup` at startup.

## High

### [High] H1 — Rebuild catches only `AudioDeviceException`; anything else becomes a 100 ms tight retry loop
- Confidence: High (verified)
- Location: `src/AdaVoice.Audio/Engine/AudioEngine.cs` `AttemptRebuild()` (229–257); `src/AdaVoice.Audio.Wasapi/WasapiDeviceFactory.cs` `Resolve` (48–64); `WasapiRenderDevice.Init/ctor`
- Problem: The factory's `Resolve` wraps only the *device resolution* lambda. Seam construction (`new WasapiRenderDevice(device)` reads `AudioClient.MixFormat` — a COM call), `Init` (`NotSupportedException` on a 44.1 kHz cable), and `Start` (`COMException`: invalidated/in-use) all sit outside it. `AttemptRebuild` catches only `AudioDeviceException`; anything else escapes to `EngineHost.ControlLoop`'s catch-all — and `_attempt`/`_nextAttemptMs` never advance, so the watchdog retries every 100 ms, forever, disposing/recreating devices each pass, with no `RebuildResult` events.
- Impact: A realistic device flap (replug at the wrong sample rate; device claimed by another app) degenerates into a permanent 10 Hz COM-churn loop with log spam and no UI feedback. Tests can't catch it: `FakeDeviceFactory` can't arm a wrong-rate cable (only `AlarmFormat` is overridable).
- Recommendation: (a) add `catch (Exception ex)` in `AttemptRebuild` treating unknown errors as transient-with-backoff; (b) move seam construction inside the factory's guarded region and translate COM errors to `AudioDeviceException`; (c) add `CableFormat` to `FakeDeviceFactory` and a test that fails today.

### [High] H2 — No unhandled-exception strategy; a crash is unlogged and Serilog never flushes
- Confidence: High (verified)
- Location: `src/AdaVoice.App/App.xaml.cs` (whole file)
- Problem: No `DispatcherUnhandledException`, `AppDomain.UnhandledException`, or `TaskScheduler.UnobservedTaskException`. Unguarded synchronous command paths exist (`SaveTake` → `WavFile.Save` disk-full; `Delete` → `File.Move` on a locked WAV; `StartRecording` → `CreateCapture` when the mic vanished). `Log.CloseAndFlush()` runs only on the clean exit path.
- Impact: Any of these crashes the app mid-call with no dialog, no log entry for the exception, and buffered log events lost — defeating the log's stated purpose ("blind GUI run still diagnosable"). The handoff itself acknowledged this gap; the point fix (Import catch) went in, the systemic fix did not.
- Recommendation: Add all three handlers in `OnStartup`; for `DispatcherUnhandledException`, log + show a message + `Handled = true` (for this product, "log, tell, keep running" beats "crash").

### [High] H3 — Crash-restart registered only in the throwaway console host
- Confidence: High (verified)
- Location: `src/AdaVoice.Host/Program.cs:11` vs `src/AdaVoice.App/App.xaml.cs`; `NativeMethods` is `internal` to Host
- Problem: Design 03 requires `RegisterApplicationRestart` ("the mic-forwarding process must not stay dead"). Only `Program.cs` calls it. The WPF app — the real product — never does.
- Impact: A crash mid-call leaves the operator with no mic path to the cable and no automatic relaunch.
- Recommendation: Expose the P/Invoke and call it in `App.OnStartup`. Note WER only restarts processes alive ≥ 60 s.

### [High] H4 — No single-instance guard
- Confidence: High (verified — no mutex anywhere in `src`)
- Location: `src/AdaVoice.App/App.xaml.cs`
- Problem/Impact: A second instance (easy: double-launch, or WER restart racing a manual restart) builds a second shared-mode graph — the caller hears the mic **twice** (summed, offset = comb-filtered echo), silently. Also: second instance's Serilog file sink fails silently on the locked log file (no logs at all); `library.json`/`settings.json` become last-writer-wins; the stop hotkey stays bound to the first instance while the operator looks at the second.
- Recommendation: Named mutex in `OnStartup` (`Local\AdaVoice`); on conflict, activate the existing window and exit.

### [High] H5 — `PreviewTake` blocks the UI thread for the whole take (STOP and hotkey dead)
- Confidence: High (verified)
- Location: `src/AdaVoice.App/ViewModels/BoardViewModel.cs` `PreviewTake` (373–378); `src/AdaVoice.Host/EngineHost.cs` `Preview` (390–425)
- Problem: `Preview` is documented as blocking (`done.Wait(durationMs + 1000)`), and `PreviewTake` calls it synchronously from a RelayCommand. Worse: the render seam was constructed on the UI thread, so NAudio posts `PlaybackStopped` to the (blocked) Dispatcher — the completion signal can never arrive, and the wait always burns the full backstop. The `"Previewing…"` notice is assigned *after* playback ends. The same class does it correctly three methods earlier (`TestOnHeadphones` uses `Task.Run`).
- Impact: A 30 s take freezes the window ~31 s. The message pump is stalled, so the on-screen STOP **and** the WM_HOTKEY-based global stop are dead — the worst possible hang for this product. Windows shows "Not Responding" after 5 s.
- Recommendation: `async Task PreviewTake()` with `await Task.Run(...)`, set the notice before starting; mirror `TestOnHeadphones`.

### [High] H6 — `TestOnHeadphones` lets background exceptions crash the app
- Confidence: High
- Location: `BoardViewModel.TestOnHeadphones` (267–277); `EngineHost.PreviewEntry`
- Problem: Only "missing file" is converted to a message. `WavFile.Load` on a corrupt WAV, `DefaultRender()` with zero output devices, and COM errors propagate; the generated `AsyncRelayCommand` rethrows on the UI thread — with H2, that's a process crash. `CalibrationStepViewModel` shows the correct pattern (`catch (Exception ex) when (ex is not OutOfMemoryException)`).
- Recommendation: Same broad catch → `Notice`.

### [High] H7 — Host seam drops the engine's error message; a failed Start is invisible
- Confidence: High (verified)
- Location: `src/AdaVoice.Host/EngineHost.cs` `OnEngineEvent` (465–479); `IPlaybackHost.StateChanged` (`EventHandler<EngineState>`)
- Problem: The engine deliberately attaches the failure reason (`SetState(Stopped, ex.Message)` — e.g. "cable not at 48 kHz"). The host logs it, then re-raises only the bare `EngineState`. `StatusViewModel` can render nothing but "Stopped". The engine also does not auto-retry a cold Start (documented v1 limit, deferred to the host — which doesn't implement it either), so a human must act — and the human never sees why.
- Impact: The #1 field failure (VB-Cable missing/misconfigured) presents as "press Start → nothing happens".
- Recommendation: Widen the event to `record EngineStateChange(EngineState State, string? Error)`; show the error in the status bar.

### [High] H8 — `DuckingOptOut` COM interop is missing `[PreserveSig]` on every method
- Confidence: High (verified against the source; masked-on-x64 reasoning is standard interop semantics)
- Location: `src/AdaVoice.Audio.Wasapi/Interop/DuckingOptOut.cs` (interfaces 58–101, call sites 26–38)
- Problem: On `[ComImport]` interfaces `PreserveSig` defaults to **false**, so `int GetDevice(string, out IMMDevice)` marshals as native `HRESULT GetDevice(LPCWSTR, IMMDevice**, int* retval)` — one parameter more than the real method, for every method in all four interfaces. On x64 the extra arg is ignored by the callee (works by accident); on x86 it would be a stack imbalance. The returned `int` is a value native code never writes, so every `Marshal.ThrowExceptionForHR(...)` checks garbage; real failures surface as marshaler-thrown `COMException`s instead.
- Impact: Latent platform bug in the shim that protects the cardinal "Windows must not duck the cable" behavior; its error handling has never actually executed as written. Vtable order is correct — only the signature convention is wrong.
- Recommendation: Add `[PreserveSig]` to every method (keeping `int` + `ThrowExceptionForHR`), or drop the `int` returns and rely on HRESULT translation.

### [High] H9 — Import can silently overwrite existing WAV recordings
- Confidence: High (verified)
- Location: `src/AdaVoice.Core/Storage/LibraryArchiveService.cs` `ExtractAudio` (128–138), `Import` (85–93)
- Problem: Imported filenames are flattened (kills traversal — good) but not bound to the phrase id, and extraction uses `ExtractToFile(dest, overwrite: true)`. In Merge mode "added" means *new id* only: a phrase with a fresh id but an existing `fileName` overwrites an unrelated existing phrase's audio. Ids are 8 hex chars (32 bits), so cross-machine collisions are plausible, not just malicious.
- Impact: Violates the module's own core invariant ("voice recordings are irreplaceable… never destroyed"); the only net is the daily backup.
- Recommendation: Re-key audio on import — extract to `{phrase.Id}.wav` and rewrite `FileName` — so filename clashes become impossible; or refuse when the destination exists and belongs to another phrase.

### [High] H10 — Drift events run host logging on the audio threads (under the mixer lock on underrun)
- Confidence: High (verified)
- Location: `src/AdaVoice.Audio/Engine/AudioEngine.cs` `OnDrift` (393–394) vs the `Events` doc (69); `MicPassthrough` (79, 85–89); `EngineHost.OnEngineEvent` (logs `DriftLogged`)
- Problem: `Drift` fires on the capture thread (overrun) and on the render thread inside `UnderrunWatch.Read` — i.e. under the mixer's lock. `OnDrift` re-raises **synchronously** (`Raise`, not `Post`), contradicting the documented "raised on the control thread" contract that the host trusts: it does file logging for drift events.
- Impact: Blocking I/O on the audio hot path exactly when timing is already bad; can amplify glitches and even trip the 500 ms stall watchdog into a needless Degraded. The fault path (`OnCaptureStateChanged` → `Post`) shows the correct pattern.
- Recommendation: Add `EngineCommand.DriftNoticed(DriftKind)` and post it; raise `DriftLogged` from the handler.

### [High] H11 — Test blind spots: WASAPI layer 0 tests, EngineHost 0 tests, fakes can't see lifecycle bugs
- Confidence: High (verified: no test project references `AdaVoice.Audio.Wasapi`; `EngineHost` hard-codes `WasapiDeviceFactory`/`WasapiDeviceMonitor`/`SystemEngineClock`/`AdaVoicePaths.DefaultRoot`)
- Problem: (a) The entire WASAPI project is untested, including pure logic (`WasapiDeviceMonitor.MapState` — per its own comment "the path that actually drives recovery"). (b) `EngineHost` is structurally untestable (would write to the live `%LOCALAPPDATA%`) and holds real rules with zero coverage — including the cardinal "preview must never reach the cable" refusal. (c) App tests run against `FakePlaybackHost`, a hand-written mirror nothing verifies; engine fakes have empty `Dispose()`, don't enforce Init-before-Start, and raise no Stopped events — the device-lifecycle bug class (exactly what this engine exists to handle, and where C1/H1 live) is invisible to the suite.
- Recommendation (ordered): 1) the failing-today rebuild test from H1; 2) `WasapiDeviceMonitorTests` driving `IMMNotificationClient` directly; 3) inject factory/monitor/clock/dataRoot into `EngineHost` and port key scenarios; 4) add `DisposeCount`/Init-guard to the fakes and assert disposal in rebuild tests; 5) `BackupService` failure-path tests.

## Medium

### [Medium] M1 — `StartRecording` blocks the UI thread up to 2 s and can strand the engine OFF AIR
- Confidence: High
- Location: `BoardViewModel.StartRecording` (345–353); `EngineHost.TryStartRecording` (221–234), `WaitForState` (434–445)
- Problem: Synchronous RelayCommand → `EnterOffAir()` + a `Thread.Sleep(5)` poll loop on the dispatcher, then `CreateCapture` inline. Failure paths: if `WaitForState` times out but the engine reaches OffAir *late*, nobody exits OFF AIR (mic muted to the call, UI thinks nothing happened); if `CreateCapture` throws (mic vanished), the exception leaves the engine OFF AIR **and** crashes via H2.
- Recommendation: `Task.Run` the call; on any failure after `EnterOffAir()`, issue `ExitOffAir()`; catch and surface errors.

### [Medium] M2 — `Calibrate` records without going OFF AIR; calibration flow is blocking
- Confidence: High (guard absence); Medium (impact)
- Location: `EngineHost.Calibrate` (118–142)
- Problem: `TryStartRecording` enforces decision #11 (record only off-air); `Calibrate` opens a second capture and records 5 s (`Thread.Sleep`) with no state check and no guard against a concurrent recording take. Reachable while Live from both the wizard and Settings.
- Impact: The passthrough keeps forwarding the mic during calibration; the person on the call hears the calibration speech; two simultaneous captures on drivers that dislike it.
- Recommendation: Mirror the recording flow: OFF AIR + wait, refuse if `_recorder is not null`, restore state after.

### [Medium] M3 — `Recorder.Stop` duration math overflows `int` at ~44.7 s
- Confidence: High (verified)
- Location: `src/AdaVoice.Audio/Recording/Recorder.cs:79`
- Problem: `trimmed.Length * 1000 / AudioFormats.SampleRate` in `int`. Overflow at >2,147,483 samples (44.7 s @48 kHz). `EngineHost.Preview:421` does the same math correctly with `1000L`.
- Impact: Negative/garbage `DurationMs` persisted in library metadata and shown on tiles. 45+ second script phrases are realistic.
- Recommendation: `(int)(trimmed.Length * 1000L / AudioFormats.SampleRate)`.

### [Medium] M4 — Entering OFF AIR doesn't stop the active phrase; its tail plays into the call later
- Confidence: High
- Location: `AudioEngine.HandleEnterOffAir` (338–345), `HandleStopPhrase` (321–327)
- Problem: OFF AIR only closes the gate; the phrase keeps consuming (inaudibly, mic ducked the whole time) and `StopPhrase` is refused while OffAir. Exit OFF AIR mid-phrase → the remainder plays into the live call unexpectedly.
- Recommendation: `_player!.Stop()` in `HandleEnterOffAir`; allow `StopPhrase` while OffAir.

### [Medium] M5 — `ExitOffAir` is dropped while Degraded; recovery returns to OFF AIR the operator tried to leave
- Confidence: High
- Location: `AudioEngine.HandleExitOffAir` (347–354), `EnterDegraded` (214–222); `EngineHost.StopRecording` calls `ExitOffAir` unconditionally
- Problem: `_restoreState` is captured at fault time; `ExitOffAir` during Degraded early-returns. After recovery the engine restores OffAir — her voice silently doesn't reach the call until she toggles again.
- Recommendation: Let Enter/ExitOffAir update `_restoreState` while Degraded.

### [Medium] M6 — Stale `CableGate.LastReadMs` after a cable rebuild can re-trip the stall watchdog
- Confidence: Medium
- Location: `AudioEngine.AttemptRebuild` (253–256), `HandleWatchdogTick` (196–199)
- Problem: The gate survives the rebuild with a pre-stall timestamp (necessarily >500 ms old). If the new render thread's first read lands after the next 100 ms tick, the engine immediately re-degrades — alarm blip, `_attempt` reset, possible flapping on slow drivers.
- Recommendation: Reset the gate's stall clock on rebuild success.

### [Medium] M7 — The DEGRADED alarm is fragile exactly when it's needed
- Confidence: High (code); Medium (frequency)
- Location: `AudioEngine.StartAlarm` (279–299); `Dsp/ChannelAdapter.cs` (13–23)
- Problem: (a) `ChannelAdapter` supports only 1→1, 1→2, N→N — a 5.1/7.1 default output (6/8-channel mix format) makes `Init` throw, the blanket catch nulls the alarm, and those machines get **no audible alarm, ever**. (b) The alarm render's `StateChanged` isn't subscribed and a failed `StartAlarm` is never retried during a long Degraded spell.
- Recommendation: Generalize mono→N up-mix; retry `StartAlarm` from the watchdog while Degraded when the alarm is null/faulted.

### [Medium] M8 — `WavFile`: no format validation on Load, no clamping on Save
- Confidence: High (Load); Medium (Save)
- Location: `src/AdaVoice.Audio/Storage/WavFile.cs` `Load` (14–25), `Save` (36–40)
- Problem: `Load` never checks `WaveFormat` — a hand-replaced 44.1 kHz/stereo file in the user-visible `audio/` folder plays at wrong pitch or as interleaved garbage into a live call. `Save` writes floats to 16-bit via NAudio `WriteSample`, which does not clamp — samples >±1.0 (mic boost, resampler overshoot) wrap to loud cracks baked into the take.
- Recommendation: Validate/convert on Load; `Math.Clamp(s, -1f, 1f)` on Save.

### [Medium] M9 — Import is not transactional and validates almost nothing
- Confidence: High
- Location: `LibraryArchiveService.Import` (85–94), `Merge` (99–110); `LibraryValidator.cs`
- Problem: (a) `ExtractToFile` mid-loop can throw (corrupt entry, disk full) — the exception escapes despite the documented "`Success` false means nothing was changed" contract; some WAVs are landed, metadata isn't. (b) The only structural checks are "parses" and `Version == 1`: duplicate ids *within* the archive, dangling `CategoryId`s, a Replace-mode library without `c-default` (which `DeleteCategory` assumes exists), and blank filenames all pass through. (c) Merge silently drops the archive's tag colours and mutates the current `Library` in place.
- Recommendation: Wrap extract+save → return `ImportResult(false, …, ex.Message)`; extract via temp names moved into place after all succeed; add a ~20-line normalization pass (dedupe ids, ensure default category, remap unknown categories, drop blank filenames, merge tags).

### [Medium] M10 — No zip resource limits (decompression bomb / disk fill)
- Confidence: High
- Location: `LibraryArchiveService.ReadEntry` (140–144), `ExtractAudio` (137); `BackupService.TryReadLatestLibrary`
- Problem: `library.json` is read fully into a string and WAV entries extracted with no size/count caps. A crafted archive (deflate ~1000:1) causes OOM or disk fill. Local DoS only, but "restore a backup someone sent me" is a realistic operator flow.
- Recommendation: Check `ZipArchiveEntry.Length` against caps before reading/extracting; cap total.

### [Medium] M11 — Logs written under the install directory
- Confidence: High
- Location: `App.xaml.cs:23`, `Program.cs:13` (`AppContext.BaseDirectory\logs`)
- Problem: Under `Program Files` the folder isn't user-writable; the Serilog file sink fails **silently** (no SelfLog) — all diagnostics vanish exactly in the deployed configuration, while user data correctly lives in `%LOCALAPPDATA%\AdaVoice`.
- Recommendation: `AdaVoicePaths.DefaultRoot\logs`, shared by both hosts.

### [Medium] M12 — `EngineHost.Dispose` ignores the join timeout, then disposes anyway
- Confidence: High (code); Medium (likelihood)
- Location: `EngineHost.Dispose` (523–539)
- Problem: `_controlThread.Join(2s)` result is discarded; if a handler is wedged in a blocking COM call, `TeardownGraph()` runs concurrently with the live handler on the same device fields — a double-dispose race on native objects at shutdown.
- Recommendation: If the join times out, log and skip engine teardown (let process exit reclaim it).

### [Medium] M13 — COM object churn and an unguarded notification path in the WASAPI layer
- Confidence: High (verified against NAudio 2.2.1 docs: `MMDevice.AudioClient` returns a NEW client each call)
- Location: `WasapiRenderDevice.cs:35`, `WasapiEnvironmentProbe.cs:27`, `EngineHost.Preview:404` (leaked `AudioClient` per call); `WasapiDevices.FindByName`/`ById` (all non-matching `MMDevice`s leaked; one flaky endpoint's `FriendlyName` throw aborts the scan); `WasapiDeviceMonitor.Raise` (83–84, unguarded on the COM callback thread — a subscriber throw is silently swallowed by the CCW, losing e.g. the fast-path `Added` event)
- Impact: Slow COM leaks concentrated in the flapping-device scenario the layer explicitly cares about; silently lost device notifications degrade recovery to the 5 s poll.
- Recommendation: `using var audioClient = device.AudioClient;`; explicit enumerate-dispose-non-matches loop with per-device try/catch; try/catch+log in `Raise`.

### [Medium] M14 — Board goes stale after import; broken-phrase flags never refresh
- Confidence: High
- Location: `BackupSettingsViewModel.Import` (84–86); `BoardViewModel` ctor (99–101); `EngineHost.PlayEntry` (192–214)
- Problem: `Phrases` and `BrokenPhraseIds` are one-time snapshots. A Replace import can remove WAVs backing tiles still shown; `Play` passes the VM gate and `PlayEntry` only logs the missing file — the operator clicks a tile and *nothing happens, silently*. The "restart to see them" message is the only mitigation.
- Recommendation: `ILibraryHost.Reloaded` event → board rebuilds; make `PlayEntry` return a status the VM can show.

### [Medium] M15 — UI validation and error-path gaps
- Confidence: High
- Location: `BoardViewModel.SaveTake` (380–392, empty title accepted — `PhraseLibraryService.Add` doesn't validate titles although categories get `RequireName`); `BackupSettingsViewModel` (catch filters narrower than the "catch-all" the commit message claimed; `OpenBackupFolder` completely unguarded — `Win32Exception` if the folder is missing); `CategoriesViewModel` (duplicate names allowed; blank rename fails silently)
- Recommendation: `CanExecute` on SaveTake; `catch (Exception ex) when (ex is not OutOfMemoryException)` on the leaf commands; duplicate-name check.

### [Medium] M16 — `ISettingsHost` is a grab-bag with three persistence conventions
- Confidence: High
- Location: `ISettingsHost.cs` (17 members); `EngineHost.cs` (295–383)
- Problem: One seam mixes preferences, window placement, wizard flag, library export/import, backup date, and `Process.Start`. Some setters persist immediately, some don't, and `SaveSettings()` writes the whole record — so an in-memory-only change gets persisted by whichever unrelated save fires next. The VMs already call `SaveSettings()` after every set, so the documented split isn't even exercised — contract drift.
- Recommendation: At the next touch: split (`IAppSettings` / window-state / backup concerns), pick persist-on-set everywhere.

### [Medium] M17 — `PhrasePlayer`: natural phrase-end can un-duck after a new phrase started
- Confidence: High (exists); window is microseconds
- Location: `PhrasePlayer.Play` (68–97) / `OnMixerInputEnded` (128–145)
- Problem: Both update `_active` under `_sync` but act on `_mic` outside it. Interleaving: A ends (sets `_active=null`, pre-empted) → `Play(B)` ducks → A's handler resumes and un-ducks while B plays; the UI also gets a stale `null` glow event.
- Recommendation: Re-check `_active` under `_sync` immediately before un-ducking (`_mic.Duck` doesn't touch the mixer, so the documented lock-order rule still holds).

## Low (grouped)

**Audio**
- Un-duck uses constructor-time `_options.DuckRampMs`, ignoring the live-updated value (`PhrasePlayer.cs:142`).
- Overrun policy clears the *whole* mic backlog → guaranteed follow-up underrun gap (`MicPassthrough.OnDataAvailable`); trim to a target backlog instead.
- `Recorder`: `_samples.Clear()` in `Start` without `_sync`; unbounded growth (~11.5 MB/min) with no max-take cap; per-element `Add` where `AddRange(span)` exists.
- `UnderrunWatch` TOCTOU + resampler-internal buffering → advisory counts can be off.
- No limiter after the mixer; duck slider allows 0 dB, so mic+phrase can clip at the endpoint (default −12 dB keeps it safe).
- `AudioEngine.State` / `EngineHost._settings` cross-thread reads are benign but undocumented — one comment each.

**WASAPI**
- `DuckingOptOutError` is never read anywhere — the "Windows ducks the cable" failure mode is completely silent (`WasapiRenderDevice.cs:47,74–78`).
- `WasapiCaptureDevice.Start` can report `Running` after `Faulted` (state is advisory; document it).
- `WasapiDeviceMonitor` comment claims "the host dedupes by device id" — it doesn't; the engine's state guard is what saves it. Fix the comment before someone "simplifies" the guard.
- Capture seam unsubscribes `RecordingStopped` before disposing → final Stopped transition unreported.

**Core**
- Atomic rename isn't power-loss durable (no flush-to-disk) — mitigated by quarantine+backup recovery; optional hardening.
- 32-bit ids with no collision check (`NewId`); loop-until-unused is one line.
- Residual filename edges after flattening: ADS names (`take:hidden.wav`), reserved device names (`CON.wav`), empty result.
- Merge-import drops tag colours; mutates `current` in place (trap for a future caching repository).
- `Export` throws on a bare relative path (`Path.GetDirectoryName("x.zip")` → `""`).
- Backup recovery restores metadata only — a phrase deleted after the backup resurrects as "broken" though its WAV is one rename away.
- `TryDelete` copy-pasted ×4; identical `JsonSerializerOptions` defined twice; live internal `List<T>` exposed as `IReadOnlyList<T>`; service constructor does I/O and can *write* (tag migration) — unhelpful failure on read-only disks; `RequireName`/`RequireTitle` duplicated.

**App/Host**
- `Win32HotkeyRegistrar` disposed after HWND death (`Closed`) — no-op work; unregister in `OnClosing` if ever reused.
- `HexToBrushConverter` and `ColorContrast.TryParseHex` parse different hex dialects → hand-edited `#AARRGGBB` gives white-on-white text.
- Duck-slider keyboard edits lost if the window closes via X (persist only on DragCompleted/LostFocus); language combo can write back null for unknown codes (the repo has already been bitten by ComboBox coercion once — `CategoryRowViewModel.ColorOptions` comment).
- `EnvironmentChecksStepViewModel` ctor runs live device probing synchronously (also on startup); `PlayEntry` does WAV load + gain loop per click on the UI thread — fine today, cache later.
- Dead/duplicate: `StatusViewModel : IDisposable` never disposed; `SetupWizardViewModel.Completed` written, never read; duplicated hotkey-unavailable string and duration-format string; `ConfirmAndRestart` briefly runs two instances and the old one's placement write is lost.
- `WindowPlacement.ClampTo` clamps to the virtual-screen *union* — L-shaped multi-monitor layouts can restore into a dead zone; no floor on hand-edited negative width.

**Tests**
- `SystemEngineClockTests` is the suite's only real-timer test; `Timer.Dispose()` doesn't wait for in-flight callbacks → narrow CI flake window. Use `Dispose(WaitHandle)`.
- `LevelsSettingsViewModelTests` calibration-wiring test cannot fail for what it claims to prove (fresh VM always has `CanAdvance == false`).
- `AlarmToneTests` never assert repetition or that the tone respects a 44.1 kHz format — the exact property the engine's alarm fix depends on.
- Cheap wins untested: `HexToBrushConverter.TryParse`, `DeviceChangeKind.DefaultChanged` engine path, `BackupService` "never throws" contract, `Recorder` stop-without-start.

---

# What to fix now vs later

**Now (small, high value):**
C1 (mic-duck relay), C2 (gate mutators on ReadError), H1 (catch-all + factory translation + the failing test), H2/H3/H4 (≈30 lines total in `App.OnStartup`), H5/H6 (mirror `TestOnHeadphones`), H7 (widen one event), M3 (one `L`), M11 (log path), H8 (`[PreserveSig]` ×4 interfaces).

**Next touch:** H9 + M9/M10 (one import-hardening pass), M1/M2 (async recorder/calibration flow + OFF AIR restore), M4–M7 (one engine-recovery hardening pass + tests), H11 items 1–4, M13 (one WASAPI hygiene pass).

**Wait for need:** M16 seam split, BoardViewModel ctor grouping (`IBoardDialogs`), orphan-audio tooling, SQLite, durability flush, localization retrofit (already planned as slice 4).

---

# What to learn from this

- **Architecture lesson:** the bugs cluster where object lifetimes cross the rebuild boundary. Every object that *survives* a partial rebuild (`_mixer`, `_gate`, `_player`) or *references* something that doesn't (`_player → _passthrough`) needs an explicit re-wiring audit. Recovery paths deserve the same test rigor as happy paths — they run on the worst day.
- **Design lesson:** a threading contract that lives in doc comments ("fires on the control thread") will drift (H10). Where you can, make the compiler enforce it: blocking host methods should return `Task`; events that must be marshaled should carry that in their type or be marshaled at the seam.
- **Engineering lesson:** guards applied at one call site rot (C2: one gated save out of seven; H6 vs the correct calibration catch; `TestOnHeadphones` vs `PreviewTake`). When you find yourself writing the same guard twice, move it into the contract — a base handler, a wrapper, or the seam itself.
