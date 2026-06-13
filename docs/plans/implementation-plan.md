# AdaVoice — Implementation Plan

**Question this answers:** *how do I actually build each phase — projects, modules,
interfaces, files, and the order to write them in?*

This is the **execution-detail** layer. It does not restate the timeline or the go/no-go
gates — those live in the [MVP roadmap](../roadmaps/mvp-roadmap.md) (strategy). It builds on
the architecture in [design 03](../design/03-architecture.md), the device seams in
[design 08](../design/08-testing.md), the audio engine in [design 06](../design/06-audio-engine.md),
and storage in [design 04](../design/04-data-storage.md).

> **Status:** not started — no solution or project files exist yet. Begins after the Phase 0
> go/no-go (see [handoff.md](../../handoff.md)).

---

## 1. Solution structure

Keep the project count low (solo dev), but split the **audio core from WPF** so the core is
testable without a UI. Recommended layout:

```
AdaVoice.sln
├── src/
│   ├── AdaVoice.Core/        Domain + audio core + app services. NO WPF reference.
│   │                         Hardware only behind IAudioCaptureDevice / IAudioRenderDevice.
│   │   ├── Domain/           Phrase, Category, Settings, engine state enum
│   │   ├── Audio/            AudioEngine, MicPassthrough, PhrasePlayer, Recorder, DeviceMonitor
│   │   ├── Audio.Naudio/     Production device impls (WasapiCapture/Out wrappers) + ducking interop
│   │   ├── Storage/          JSON repository (atomic write), BackupService
│   │   └── Services/         PhraseLibraryService, SettingsService, HotkeyService, LocalizationService
│   └── AdaVoice.App/         WPF: Views (XAML), ViewModels, .resx (uk/pl/en), DI wiring, entry point
└── tests/
    ├── AdaVoice.Core.Tests/      State machine, mixer, services (fake devices)
    ├── AdaVoice.Dsp.Tests/       Golden-file DSP (trim, loudness, fade)
    └── AdaVoice.Storage.Tests/   Atomic write, corruption, orphan, import
```

**Why this split:** the core is the reliability-critical part and must run in CI against fake
devices ([design 08 §1](../design/08-testing.md)). WPF cannot run headless in CI easily, so
keeping it in a separate project that *references* Core (never the reverse) protects the test
loop. `Audio.Naudio` is a sub-folder, not a separate project, to avoid project sprawl — but it
is the only place `WasapiCapture`/`WasapiOut` appear.

**Dependency direction (must hold):** `App → Core`. Core never references App or WPF.
Storage and Services never reference Audio device impls directly — only the interfaces.

## 2. Build-order principles

1. **Seams before implementations.** Define `IAudioCaptureDevice` / `IAudioRenderDevice` /
   `IDeviceMonitor` first, then build fakes, then real NAudio wrappers. This lets the engine
   be written and tested before touching hardware.
2. **Test-with-the-feature.** Each component ships with its tests in the same phase
   ([design 08 §5](../design/08-testing.md)) — coverage is not deferred.
3. **Riskiest first.** The audio engine (Phase 1) is built before storage and UI, because it
   is where the project lives or dies.
4. **Reuse the one spike keeper.** `spike/AdaVoice.Spike/DuckingOptOut.cs` is the reference
   for the `SetDuckingPreference` interop — port it into `Audio.Naudio`. Everything else in
   `spike/` is throwaway.

## 3. Phase-by-phase build steps

Phases and their exit criteria are defined in the [roadmap](../roadmaps/mvp-roadmap.md). Below
is *what to build, in what order*, inside each.

### Phase 0 — Spike (code done; execution pending)

- Code exists in `spike/`. Remaining work is **running it** on hardware — see
  [spike/README.md](../../spike/README.md) and [handoff.md](../../handoff.md). No production
  code in this phase.

### Phase 1 — Audio core + tests

Build order:

1. **Solution + projects + CI** — create the layout above; wire a CI workflow that runs the
   unit + golden-file suites on every commit (required from Phase 1).
2. **Domain types** — engine state enum (`Stopped / Live / OffAir / Degraded`), value types
   for formats and levels.
3. **Device seams** — `IAudioCaptureDevice`, `IAudioRenderDevice`, `IDeviceMonitor`
   ([design 08 §1](../design/08-testing.md)) + test doubles: `FileCaptureDevice`,
   `MemoryRenderDevice`, `FaultyDevice`, synthetic `IDeviceMonitor`.
4. **`PhrasePlayer` + mixer** — single-playback rule, 10 ms stop fade, duck ramp. Test against
   `MemoryRenderDevice` (golden files for the fade).
5. **`MicPassthrough`** — capture → mono/48k → duck branch → mixer.
6. **`AudioEngine`** — owns the three streams, the state machine, watchdog (rebuild on
   >500 ms pull stall), drift policy (drop-oldest / insert-silence, logged), DEGRADED alarm on
   the system default device, `RegisterApplicationRestart`. Drive every transition with
   `FaultyDevice`.
7. **`Recorder`** (DSP) — trim, RMS loudness-match to calibrated reference (peak ceiling
   −3 dBFS), OFF AIR enforcement. Golden-file tests.
8. **`Audio.Naudio` production impls** — wrap `WasapiCapture`/`WasapiOut`; port the ducking
   opt-out interop from the spike. Invoke opt-out on every stream (re)start.
9. **8-hour soak** on real hardware (engine only, no UI) — drift events < a few/hour;
   unplug/replug recovers.

### Phase 2 — Library + storage

1. `IPhraseRepository` + JSON implementation with **atomic write** (tmp + rename).
2. Startup validation + recovery path (corrupt `library.json` → load newest backup, surface
   message, never start silently empty).
3. Orphaning delete (`deleted-{id}.wav`), missing/corrupt-WAV broken-phrase flagging.
4. `BackupService` — daily zip incl. `audio/` (keep 7); manual export/import (orphans excluded
   from export).
5. Tests: automated kill-9 simulation, corruption recovery, export→import round-trip.

### Phase 3 — Board + Recorder UI + localization

1. DI/bootstrap in `AdaVoice.App`; `StatusViewModel` bound to engine state events.
2. Localization spine first — `.resx` for uk/pl/en + the completeness test; **no hard-coded
   XAML strings from the first view onward**.
3. `BoardViewModel` + Board view — large phrase buttons (enable as background decode lands),
   Topmost toggle (default on), status bar, big STOP. Build **Full and Docked** layouts on the
   [design 09](../design/09-design-system.md) tokens.
4. `RecorderViewModel` + Recorder view — OFF AIR banner, record/re-record, preview to monitor.
5. All interaction states from [design 05 §2](../design/05-ui-design.md) (first-run welcome,
   decode-dimmed, broken-phrase, empty search/category, toasts).
6. **Operator pilot** (½ day) after this phase — the only acceptance gate before late phases.

### Phase 4 — Stop hotkey + Settings + wizard

1. `HotkeyService` — `RegisterHotKey` via `HwndSource`; `Pause` default + `Ctrl+F12` fallback;
   conflict surfaced as a typed error.
2. `SettingsViewModel` + grouped Settings IA (Levels → Behavior → Language & Backup → Devices
   with confirm-on-change); live duck sliders, device meters, re-run calibration.
3. Setup wizard — all environment checks, loopback self-test, first-call confidence card
   (decision #24).

### Phase 5 — Hardening + installer

1. Edge cases from [design 07](../design/07-risks-security.md); Serilog rolling-file logging +
   engine-state alarms.
2. Inno Setup self-contained .NET 10 installer; short user guide (fallback playbook, Zoho mic
   screenshots, SmartScreen note).
3. Final manual call-test checklist ([design 08 §4](../design/08-testing.md)); pilot follow-up.
4. → hand to the [production-readiness plan](production-readiness-plan.md) for the release gate.

## 4. Cross-cutting rules (apply in every phase)

- No audio work on the WPF dispatcher; UI↔engine via immutable commands + marshalled events
  ([design 03 §4](../design/03-architecture.md)).
- Devices stored by MMDevice ID with friendly-name fallback — never guess on renumbering.
- Every user-facing string goes through `.resx` from day one.
- A phase's code does not merge without its tests green in CI.
