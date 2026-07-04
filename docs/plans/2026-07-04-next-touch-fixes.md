# "Next touch" fixes — 2026-07-04

Source: `docs/reviews/2026-07-04-full-codebase-review.md`, "Next touch" list (H9 already done
in the top-10 pass). All findings re-verified against current source before this plan.

## Passes (in order)

### Pass 1 — Engine recovery hardening (M4–M7), `src/AdaVoice.Audio`
- **M4** `HandleEnterOffAir` also stops the active phrase (`_player!.Stop()`); `HandleStopPhrase`
  allows OffAir too. The gate keeps pulling, so the fade drains and `PhraseChanged(null)` still
  fires — the UI glow clears.
- **M5** While Degraded, `HandleEnterOffAir`/`HandleExitOffAir` update `_restoreState`
  (OffAir/Live) instead of being dropped, so recovery returns to what the operator last asked.
- **M6** New `CableGate.MarkAlive()` stamps the stall clock; called on rebuild success so the
  next watchdog tick doesn't instantly re-degrade off a stale `LastReadMs`.
- **M7** (a) `ChannelAdapter` gains mono→N up-mix (new small provider) so a 5.1/7.1 default
  output no longer kills the alarm; (b) `AttemptRebuild` retries a dead alarm
  (null or Faulted) at each backoff-paced attempt — never every 100 ms tick.
- Tests: OFF AIR stops phrase; StopPhrase while OffAir; ExitOffAir during Degraded restores
  Live; no re-degrade after rebuild with a >500 ms stale gate; mono→6 up-mix; alarm retried
  after a failed first start and after a fault.

### Pass 2 — Fake hardening (H11 item 4), `tests/AdaVoice.Audio.Tests`
- `ControllableCaptureDevice`/`ControllableRenderDevice`: `DisposeCount`; render `Start()`
  throws without `Init` (mirrors the real seam's "Call Init before Start.").
- Rebuild tests assert the old device was disposed exactly once.

### Pass 3 — EngineHost injectability (H11 item 3), `src/AdaVoice.Host`
- Optional ctor params: `IAudioDeviceFactory`, `IDeviceMonitor`, `IEngineClock`, `dataRoot`
  (default to today's concrete values — zero call-site changes).
- New `tests/AdaVoice.Host.Tests` (net10.0-windows), reusing the Audio.Tests fakes via a
  project reference. Covers `LibraryWarning` mapping and (after Pass 4) recording/calibration
  OFF AIR behavior. Preview-refuses-cable stays untestable for now (static `WasapiDevices`).

### Pass 4 — Recorder/calibration flow (M1/M2), Host + App
- **M1 host** `TryStartRecording`: any failure after `EnterOffAir()` (wait timeout or throw)
  issues `ExitOffAir()` and cleans up before returning/rethrowing. `StopRecording`: restore
  runs in `finally`; a capture-dispose failure is logged, never blocks going back on air.
- **M1 VM** `StartRecording`/`StopRecording` become `async Task` + `Task.Run` + the broad
  catch → Notice pattern (mirrors `PreviewTake`). Tests switch to `ExecuteAsync`.
- **M2 host** `Calibrate`: refuse while `_recorder` is active; if Live, `EnterOffAir` +
  wait before capturing and `ExitOffAir` in `finally`; unchanged when Stopped (first-run
  wizard path). VM already runs it via `Task.Run`.

### Pass 5 — Import hardening (M9/M10), `src/AdaVoice.Core`
- **M9 transactional**: extract WAVs to `*.importing` temp names, move into place only after
  all succeed, then `Save`; any failure deletes the temps and returns
  `ImportResult(false, …, error)` — the exception no longer escapes.
- **M9 normalization** (after re-key, before Merge/Replace): drop blank-id phrases, dedupe
  ids (keep first), remap unknown `CategoryId`s to `Category.DefaultId`, ensure the default
  category exists in Replace mode.
- **M9 merge**: union the archives' tag registry (case-insensitive, keep existing colours);
  build the merged `Library` with `with {}` instead of mutating `current`.
- **M10 caps**: `library.json` ≤ 16 MB, per-WAV ≤ 256 MB, total extracted ≤ 1 GB, entries
  ≤ 10 000; `BackupService.TryReadLatestLibrary` gets the same json cap (skip → null).
- Tests: corrupt second entry → `Success=false`, nothing changed; duplicate ids; dangling
  category remap; Replace without `c-default`; tag colours survive a merge; oversized entry
  rejected.

### Pass 6 — WASAPI hygiene (M13) + monitor tests (H11 item 2)
- `using var audioClient = device.AudioClient;` at `WasapiRenderDevice` ctor,
  `WasapiEnvironmentProbe`, and `EngineHost.Preview` (NAudio returns a new RCW per access).
- `WasapiDevices.FindByName`/`ById`: explicit loop — dispose non-matches, per-device
  try/catch so one flaky endpoint no longer aborts the scan.
- `WasapiDeviceMonitor.Raise`: try/catch so a subscriber throw never goes back into the COM
  callback; fix the wrong "host dedupes by device id" comment.
- New `tests/AdaVoice.Audio.Wasapi.Tests` (net10.0-windows): drive the public
  `IMMNotificationClient` methods directly (no COM) — state mapping, default-changed
  null-guard, subscriber-throw safety.

## Verification
`dotnet build` clean; full `dotnet test` green (308 existing + new). One commit per pass.
