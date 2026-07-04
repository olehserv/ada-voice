# AdaVoice — Handoff & Progress

**Live status of the project.** Read this first when you (or a new session) pick the work
back up. It answers one question: *where are we right now?*

- **What it is:** done work, work in progress, anything interrupted, and open questions.
- **What it is not:** the plan (see [implementation plan](docs/plans/implementation-plan.md)),
  the strategy (see [roadmap](docs/roadmaps/mvp-roadmap.md)), or the decision record
  (canonical table in [design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).

_Last updated: 2026-07-04._  
_Top-10 risk fixes from the full codebase review
([`docs/reviews/2026-07-04-full-codebase-review.md`](docs/reviews/2026-07-04-full-codebase-review.md))
implemented per [`docs/plans/2026-07-04-top10-risk-fixes.md`](docs/plans/2026-07-04-top10-risk-fixes.md):
C1 (mic-duck relay across rebuilds), C2 (ReadError write guard + startup warning), H1 (rebuild
catch-all + backoff), H2/H3/H4 (global exception handlers, crash restart, single-instance mutex),
H5/H6 (async PreviewTake + broad preview catches), H7 (StateChanged now carries the error to the
status bar), H8 ([PreserveSig] on the ducking interop), H9 (import re-keys WAVs to {id}.wav),
H10 (drift posted off the audio threads), plus riders M3 (duration overflow) and M11 (logs to
%LOCALAPPDATA%). 308 tests green (66 Core + 90 Audio + 152 App), 6 new regression tests.
Not yet committed or user-smoke-tested. Settings-window slice-1 GUI checklist still open — see
Next action._

---

## Status in one line

**Design complete and reviewed. Phase 0 go/no-go gate PASSED. Phase 1 engine + Recorder + storage +
preview run live end-to-end AND verified on the target machine. Phase 2 storage (categories/tags,
delete-as-orphan, daily backups, export/import, `settings.json`, validation/recovery) is built and
tested. The WPF Board now manages the library — play/record, edit/delete, search/category-filter, a
category manager, colour-filled phrase tiles, and coloured reusable tag chips — smoke-tested end to
end by the user and merged + pushed to `main` (2026-07-02). The setup-wizard UI (environment
checks, voice calibration, hotkey status, instructions, first-call card, VB-CABLE download link,
calibration countdown ring) is also built, smoke-tested by the user, and merged + pushed to
`main` (2026-07-02). The Settings window (Levels/Behavior/Language & Backup groups — slice 1 of
the UI/UX + localization scope) is also built, fully reviewed, and merged to `main` (2026-07-02),
but not yet manually smoke-tested by the user.** Next real step: smoke-test the Settings window,
then slice 2 (interaction-state gaps) of the UI/UX pass + localization.

## Done

- ✅ **Top-10 risk fixes — implemented, all tests green (2026-07-04, uncommitted).** The full
  codebase review ([`docs/reviews/2026-07-04-full-codebase-review.md`](docs/reviews/2026-07-04-full-codebase-review.md))
  scored Reliability 5/10 with the bugs clustered in the recovery paths; all ten top risks plus
  three tiny riders are now fixed (plan:
  [`docs/plans/2026-07-04-top10-risk-fixes.md`](docs/plans/2026-07-04-top10-risk-fixes.md)).
  - **C1:** new `MicDuckRelay` — the phrase player's duck target now follows a mic rebuild
    (before: ducking landed on the disposed passthrough forever after a headset replug).
  - **C2:** `PhraseLibraryService` mutators refuse while `LoadStatus == ReadError`
    (`IsWritable`/`EnsureWritable`), so a transiently locked `library.json` can no longer be
    overwritten by the seeded stand-in; `ILibraryHost.LibraryWarning` surfaces
    ReadError/Corrupt/RecoveredFromBackup as a Board notice at startup.
  - **H1:** `AttemptRebuild` catches non-`AudioDeviceException` failures as transient-with-backoff
    (before: a wrong-rate cable replug became a 10 Hz rebuild churn loop); `FakeDeviceFactory`
    gained `CableFormat` to arm the scenario.
  - **H2/H3/H4 (App.xaml.cs):** all three global exception handlers (UI errors: log + dialog +
    keep running; fatal: log + flush), `RegisterApplicationRestart` (NativeMethods made public),
    and a single-instance mutex (second launch shows a message and exits).
  - **H5/H6:** `PreviewTake` is async (`Task.Run`, mirrors `TestOnHeadphones`) — no more frozen
    window/STOP/hotkey for the take's length; both preview commands got the broad
    `catch (Exception) when (not OOM)` → Notice pattern.
  - **H7:** `IPlaybackHost.StateChanged` widened to `EngineStateChangedEventArgs(State, Error)`;
    `StatusViewModel.StateError` + a red status-bar TextBlock show why a Start failed
    (e.g. "cable not at 48 kHz") instead of "button does nothing".
  - **H8:** `[PreserveSig]` on all 20 methods of the `DuckingOptOut` COM interfaces — the
    HRESULT checks now actually check HRESULTs; x86 would have been a stack imbalance.
  - **H9:** import re-keys every incoming WAV to `{phrase.Id}.wav` and rewrites `FileName`, so an
    archive file name can never overwrite a different existing phrase's recording.
  - **H10:** drift events are posted through the command queue (`EngineCommand.DriftNoticed`) —
    host file logging no longer runs on the capture/render threads (or under the mixer lock).
  - **Riders:** M3 (`Recorder` duration math no longer overflows at ~44.7 s), M11 (both hosts log
    to `%LOCALAPPDATA%\AdaVoice\logs`, not the install dir).
  - 308 tests green (66 Core + 90 Audio + 152 App); 6 new regression tests (C1 rebuild-duck,
    H1 churn, C2 ×2, H7 error surface, H9 collision). **Not yet committed; needs a user smoke
    test** (launch twice → second instance message; preview a take → UI responsive; logs under
    `%LOCALAPPDATA%\AdaVoice\logs`).
- ✅ **Settings window — merged to `main`, pushed (2026-07-02).** Slice 1 of the UI/UX +
  localization scope
  ([`docs/plans/ui-ux-localization-scope.md`](docs/plans/ui-ux-localization-scope.md)). Design
  spec: [`docs/superpowers/specs/2026-07-02-settings-window-design.md`](docs/superpowers/specs/2026-07-02-settings-window-design.md);
  plan: [`docs/superpowers/plans/2026-07-02-settings-window.md`](docs/superpowers/plans/2026-07-02-settings-window.md).
  - A new modal `SettingsWindow` (triggered via **Settings…** next to **Setup…** in the status bar)
    with three groups: **Levels** (mic-duck slider, moved here from the Board's status bar; re-run
    voice calibration reusing the wizard's `CalibrationStepViewModel`/view unmodified), **Behavior**
    (always-on-top toggle — live; "new phrase stops the current one" toggle — restart-to-apply;
    read-only stop-hotkey status), **Language & Backup** (English/Українська/Polski picker —
    persists and offers a restart now, no `.resx` yet; manual export/import via
    `LibraryArchiveService`; last-backup-date readout; open-backup-folder).
  - Built via subagent-driven development: 7 tasks, each independently implemented and reviewed,
    plus a final whole-branch review. The final review caught one real gap — `BackupSettingsViewModel
    .Import` was missing the same unexpected-exception catch-all `Export` already had, a genuine
    crash path (no global unhandled-exception handler exists in `App.xaml.cs`) for an operator
    importing a slightly-bad backup file — fixed before merge.
  - Deliberately deferred (own future slice, needs new audio capability): the **Devices** group
    (mic/cable/monitor pickers with live level meters — no live audio metering or `Monitor` device
    role exists anywhere yet) and the phrase **monitor slider**. Also deferred: true hotkey
    reassignment (capture-any-key) — this slice only shows which of the two fixed candidates
    (`Pause`/`Ctrl+F12`) is active.
  - **Accepted UX trade-off:** the duck slider no longer lives inline on the Board — adjusting it
    mid-call now requires opening Settings. Matches design 05's original layout; a deliberate,
    user-approved change from what shipped before.
  - 302 tests green (63 Core + 88 Audio + 151 App) at merge. **Not yet manually smoke-tested by the
    user** — see "In progress / interrupted" above for the open GUI checklist.
- ✅ **Board library UI — merged to `main`, pushed (2026-07-02).** Surfaced the already-built library
  features in the WPF Board, across round 1 + a 3-slice round 2 driven by two rounds of interactive
  smoke feedback. All smoked and confirmed working by the user; plan:
  [`docs/superpowers/plans/2026-07-01-board-library-ui-round2.md`](docs/superpowers/plans/2026-07-01-board-library-ui-round2.md).
  - **Round 1:** new `ILibraryHost` seam (the library read-model + edits; `Phrases` moved off
    `IPlaybackHost`). Right-click **Edit…** (rename, move category, edit tags) and **Delete** (confirm →
    orphan WAV → toast); live **search** (title/tags) + **category filter** over an `ICollectionView`
    with a distinct "no matches" state; broken-phrase flag (dimmed + badge); a **"Categories…"** manager
    (add/rename/recolour/delete, default protected).
  - **Round 2 Slice 1 (bug fixes):** phrase buttons stay enabled when the engine is stopped (Play is
    gated in the VM, not the control, so the right-click menu still opens); right-click **Test on
    headphones** previews to the monitor with the engine off; engine buttons (`Start`/`Stop
    engine`/`OFF AIR`/`STOP`) reflect engine state; the window remembers its size/position
    (`WindowPlacement`, clamped to the current screens on restore).
  - **Round 2 Slice 2 (category colour fill):** phrase tiles are filled with their category colour via a
    full-bleed `Border` (immune to WPF-UI hover/press states) with one WCAG auto-contrast text brush;
    colour picking is a single-select dropdown over a 20-colour palette (`ColorPalette`), bound so an
    off-palette legacy colour can't be silently wiped; the phrase edit dialog's category picker shows
    colour too; tile duration reads in seconds ("5.7 s"). Design-09 records the neutral→filled override.
  - **Round 2 Slice 3 (colored reusable tags):** a tag→colour registry on the library (`Library.Tags` /
    `TagInfo`, case-insensitive, cycles the palette on first use, migrates pre-registry tags on load);
    the edit dialog's tag box is a chip editor (add/remove/reuse-via-suggestion); phrase tiles show tags
    as coloured chips on a fixed dark scrim so they read over any category fill.
  - 247 tests green (57 Core + 88 Audio + 102 App) at merge.
- ✅ **Setup-wizard UI — built, unit-tested, smoke-tested, merged + pushed to `main` (2026-07-02).**
  All 5 Bucket A steps implemented (environment checks, voice calibration, hotkey status,
  instructions, first-call checklist) as a modal wizard flow on top of the Board. Triggered
  automatically on first run (when `settings.WizardCompleted` is false) and re-runnable via
  **Setup…** in the status bar. Design spec:
  [`docs/superpowers/specs/2026-07-02-setup-wizard-ui-design.md`](docs/superpowers/specs/2026-07-02-setup-wizard-ui-design.md);
  build plan: [`docs/superpowers/plans/2026-07-02-setup-wizard-ui.md`](docs/superpowers/plans/2026-07-02-setup-wizard-ui.md).
  - **Follow-up (same day):** restored two spec items the build plan had silently dropped — a
    VB-CABLE download link (`https://vb-audio.com/Cable/`) shown when the cable environment check
    fails, and a purely cosmetic 5-second countdown-ring animation on the calibration step
    (View-owned, no ViewModel change). Plan:
    [`docs/superpowers/plans/2026-07-02-setup-wizard-followups.md`](docs/superpowers/plans/2026-07-02-setup-wizard-followups.md).
  - 279 tests green (58 Core + 88 Audio + 133 App) at merge.
  - **Manual GUI smoke-tested by the user and confirmed working** (first-run hotkey label,
    environment checks + VB-CABLE link, calibration + countdown ring, re-run/cancel behavior).
  - Known accepted trade-off: the VB-CABLE link identifies the cable check by matching
    `EnvironmentCheck.Name == "Cable output"` (a string, not a type) — fragile to a future rename
    in `EnvironmentChecks.cs`, deliberately not fixed with a new enum (out of scope for a cosmetic
    link).
  - Device selection, live meters, the loopback self-test, VB-CABLE install detection, and the 3
    extra environment checks remain a v2 follow-up (not started).
- ✅ **Phase 2 storage — built + tested (pre-2026-07-01; the handoff had under-recorded this).** Category
  CRUD + phrase categorization + tags, delete-by-orphan, daily backups + recover-from-backup,
  library export/import, `JsonSettingsRepository`, corrupt-library quarantine + broken-phrase flagging,
  kill-9 atomic-write test. All in `AdaVoice.Core` behind `IPhraseRepository`; covered by Core tests.
- ✅ **Hardware run of the full loop — PASSED (2026-07-01).** The engine + Recorder + storage + preview
  were run end-to-end on the target machine (`S` Live → `P` beep → `O`/`O` OFF AIR → unplug/replug the
  cable → `R` record → `V` preview → restart-reload). User confirmed everything works fine. This closes
  the standing "tested against fakes, never run on hardware" gap that hung over the engine, Recorder,
  storage, and preview slices below.
- ✅ **Global stop hotkey — merged to `main` (2026-07-01).** `Pause` with `Ctrl+F12` fallback via Win32
  `RegisterHotKey` behind an `IHotkeyRegistrar` seam; `HotkeyService` owns the policy (4 unit tests),
  `MainWindow` wires `StopRequested` → `StopCommand` (stops the current phrase only, never the engine).
  On-screen hint shows the active hotkey under the STOP button. Manual end-to-end verified (Pause stops
  the phrase while Chrome is focused). Plan: [`docs/superpowers/plans/2026-06-29-stop-hotkey.md`](docs/superpowers/plans/2026-06-29-stop-hotkey.md).
- ✅ **Duck slider + WPF-UI polish — merged to `main` (2026-07-01).**
  - Live mic-duck slider: `PhrasePlayer.SetDuck` + `EngineCommand.SetDuckLevel` (engine re-applies the
    level after a Stop/Start rebuild) → `ISettingsHost` on `EngineHost` (apply-live vs save split) →
    `SettingsViewModel` + a snapped −40..0 dB slider in the status bar (saves on drag-end).
  - WPF-UI 4.3.0 Fluent dark theme adopted: `ui:Button` appearances (STOP=Danger, Record=Primary),
    shared `PhraseButtonStyle`, finished tokens (type scale, spacing, radius; Segoe UI Variable),
    first-run welcome `ui:Card`, and a save toast (`Snackbar`). 167 unit tests green.
  - **FluentWindow chrome:** `ui:FluentWindow` + compact `ui:TitleBar`, solid design-09 background
    (no Mica). **Topmost preserved** (verified via WS_EX_TOPMOST).
- ✅ **Operator pilot prepared (2026-06-29)** — [`docs/plans/operator-pilot.md`](docs/plans/operator-pilot.md):
  a functional-smoke pilot script (record fresh live, real test call). The supervised pilot passed
  (user: "tested everything, works awesome").
- ✅ **Design phase** — 9 docs in [`docs/design/`](docs/design/README.md), eng review + design
  review both CLEARED (2026-06-10).
- ✅ **Canonical decisions** — 24 entries locked ([design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).
- ✅ **A8 permission gate** — resolved 2026-06-13: no employer/Zoho agreement needed
  (employer is loyal). No longer blocks the build.
- ✅ **Phase 0 spike — code** — throwaway console prototype committed under [`spike/`](spike/README.md)
  (mic→duck→mix→CABLE, latency self-test, ducking opt-out interop). ~656 lines. Builds for
  `net10.0-windows`.
- ✅ **Phase 0 go/no-go gate — PASSED (2026-06-15)** — tested end-to-end against a real Zoho
  Voice call on the target machine. Architecture A is confirmed; the rehearsed Voicemeeter
  fallback stays as a documented plan B but is not needed. (See "Open questions" — A5/A6/A11
  resolved.) _Detailed numbers (mouth-to-Chrome latency, AGC notes) still to be captured in
  `spike/PHASE0-RESULTS.md` — file not yet created._
- ✅ **Phase 1 audio core — partial (2026-06-15)** — production code under [`src/`](src/):
  device seams (`IAudioCaptureDevice`/`IAudioRenderDevice`), `MicPassthrough` (capture →
  format → duck), `PhrasePlayer` + `PhraseSampleProvider` (single-playback, fade-out),
  `RampGain`/`ChannelAdapter` DSP, and the WASAPI seam (`WasapiCaptureDevice`,
  `WasapiRenderDevice`, `DuckingOptOut` COM interop). 23 unit tests green against fake
  devices; CI builds + tests on every push. The seam was validated on real hardware via
  [`tools/AudioSeamCheck`](tools/AudioSeamCheck) (live mic→CABLE passthrough with ducking).
- ✅ **Phase 1 AudioEngine — complete (2026-06-22)** — the full state machine
  (Stopped/Live/OffAir/Degraded) on a single command queue: Stop+teardown, OFF AIR gate,
  Play/StopPhrase, fault→Degraded + independent alarm, targeted rebuild with exponential backoff +
  state restore, watchdog stall detection, and `DeviceChanged` + device-arrived fast path. 49 unit
  tests green against fakes (`ManualEngineClock` + `FakeDeviceFactory`); full solution builds clean.
  Code: [`src/AdaVoice.Audio/Engine/AudioEngine.cs`](src/AdaVoice.Audio/Engine/AudioEngine.cs).
  Out of scope and still to come: the real WASAPI factory, the device monitor, and the host.
- ✅ **WASAPI factory + device monitor — complete (2026-06-22)** — `WasapiDeviceFactory`
  (resolves Mic/Cable/Alarm `MMDevice`s by role, builds the existing seams, transient
  `AudioDeviceException` on a missing device) and `WasapiDeviceMonitor` (`IMMNotificationClient` →
  `DeviceChanged`, role-agnostic, emits all default-changes). Also a core fix: the DEGRADED alarm
  is now built at the alarm device's own sample rate (the system default output is often 44.1 kHz
  and the seam does not resample — previously this threw out of the control loop). Seams now
  dispose their `MMDevice` (rebuild COM-leak fix). 50 unit tests green; full solution builds clean.
  Hardware checks: `tools/AudioSeamCheck --factory` and `--monitor` (still to be run on the target
  machine).
- ✅ **Runnable host — complete (2026-06-22)** — `SystemEngineClock` (real `IEngineClock`:
  Stopwatch + Timer) and a new `AdaVoice.Host` console project. `EngineHost` wires the factory +
  monitor + clock into the engine and runs the single control loop on a dedicated thread with a
  catch-all so no handler can kill it; it maps device-monitor events to a role (best-effort,
  pragmatic v1) and disposes in a safe order. `Program` adds Serilog rolling-file logging,
  `RegisterApplicationRestart`, and keyboard controls. Also a core fix: a failed `Start` now stays
  Stopped with the error surfaced instead of crashing the (new) control thread, and `Post` ignores
  a disposed queue. 54 unit tests green; full solution builds clean. **Verified on hardware 2026-07-01.**
- ✅ **Recorder core — complete (2026-06-23)** — DSP (`Loudness` RMS/peak, `SilenceTrim`
  −45 dBFS/150 ms, `LoudnessMatch.ComputeGainDb` with −3 dBFS ceiling), `WavFile.Save` (float →
  16-bit PCM, atomic temp→final), and `Recorder` (record from a capture device → engine-format via a
  shared `Dsp.EngineFormat` converter with push-drain + resampler tail-flush → trim →
  loudness-match). Wired into the host on `[R]` (OFF AIR → record → save WAV under `recordings/`).
  72 unit tests green incl. a 44.1 kHz-stereo resample test; a capture-thread race in the Recorder
  was found and fixed (lock). **Saved takes are the raw trimmed audio; `gainDb` is metadata that
  will be applied once playback + library land.** **Verified on hardware 2026-07-01.**
- ✅ **Storage + preview (thin vertical) — complete (2026-06-23)** — new `AdaVoice.Core` project
  (net10.0, no audio dep): `Library`/`PhraseEntry`/`Category` domain, `IPhraseRepository` +
  `JsonPhraseRepository` (System.Text.Json, atomic tmp→rename, seeded default), `PhraseLibraryService`
  (WAV-first `Add`). `WavFile.Load` added (and `Save` now creates its dir). Host catalogues a take
  into `%LOCALAPPDATA%\AdaVoice\library.json` + `audio\p-….wav` on `[R]`, and `[V]` previews the last
  phrase to the default output (monitor stand-in) with `gainDb` applied — **refusing if the default
  output is the cable** (cardinal rule). 80 unit tests green. **Verified on hardware 2026-07-01.**
- ✅ **Doc structure cleanup** (2026-06-13) — removed the original brief (`1_DESIGN.md`) and
  `TODOS.md`; moved the design system into [`docs/design/09-design-system.md`](docs/design/09-design-system.md);
  added planning docs under [`docs/plans/`](docs/plans/).

## In progress / interrupted

Settings window (slice 1) is code-complete, fully reviewed, and merged. **Manual smoke test is
under way (2026-07-04):** opening the window via **Settings…** immediately threw
`InvalidOperationException` — `Run.Text` defaults to a `TwoWay` binding in WPF (unlike
`TextBlock.Text`, which is `OneWay`), and it was bound to the read-only `LastBackupDate`. Fixed with
an explicit `Mode=OneWay` in `SettingsWindow.xaml` (commit `05d2f26`); 302 tests still green. The
plan's final step
([`docs/superpowers/plans/2026-07-02-settings-window.md`](docs/superpowers/plans/2026-07-02-settings-window.md),
Task 7 Step 14) is an 11-item interactive GUI checklist (duck slider from its new home, recalibrate,
live always-on-top, restart-required labels, language + restart, export/import round-trip,
open-backup-folder) — window now opens, but the rest of the checklist still needs to be clicked
through before calling slice 1 fully done.

## Next action

**1) UI/UX pass + localization — scoped into 4 ordered slices.** Full audit + rationale in
[`docs/plans/ui-ux-localization-scope.md`](docs/plans/ui-ux-localization-scope.md):

1. ✅ **Settings window** — built (2026-07-02): Levels (duck slider moved here from the Board status
   bar, re-run voice calibration reusing the wizard's step), Behavior (always-on-top, "new phrase
   stops current" toggle, read-only hotkey status), Language & Backup (language picker — persists
   now, `.resx` retrofit is slice 4 — manual export/import, backup-folder access, last-backup-date).
   Device pickers/live meters and true hotkey reassignment deliberately deferred (design spec:
   [`docs/superpowers/specs/2026-07-02-settings-window-design.md`](docs/superpowers/specs/2026-07-02-settings-window-design.md));
   plan: [`docs/superpowers/plans/2026-07-02-settings-window.md`](docs/superpowers/plans/2026-07-02-settings-window.md).
   Built via subagent-driven development — 7 tasks each independently implemented + reviewed, one
   final whole-branch review that caught and fixed a real gap (`BackupSettingsViewModel.Import` was
   missing the same unexpected-exception catch-all `Export` already had — a genuine crash path for
   an operator importing a slightly-bad backup, since the app has no global unhandled-exception
   handler). 302 tests green (63 Core + 88 Audio + 151 App) at merge. **Manual smoke test in
   progress (2026-07-04):** found and fixed a crash-on-open bug (see "In progress" above); the rest
   of the 11-item checklist above still needs to be run before considering slice 1 fully done.
2. **Interaction-state gaps** on the existing Board (repair dialog, category-empty CTA, search
   Clear button, recorder level meter/processing state, wizard per-row spinner) — not started.
3. **Full/Docked responsive layout** — currently unbuilt (single layout at all widths); needs a
   design decision (bring back the category rail at ≥720px, or keep dropdown-only and update
   design 05) before implementation. Not started.
4. **Localization retrofit (UA/PL/EN)** — done last, after 1–3's new strings exist. Not started.

**Note (debt):** localization was deliberately deferred — every Board/dialog string added so far
(including all of slice 1's new Settings-window strings) is English-only and will need a `.resx`
retrofit (slice 4).

**Open follow-ups (named so they're not lost):**
- **Configurable monitor device:** preview currently uses the default output as the monitor
  stand-in; a real monitor device selection comes with `settings.json`.
- **2nd-capture fallback:** the Recorder opens its own mic capture; if a driver refuses a second
  WASAPI capture client, the fallback is tapping the engine's existing capture. Watch for it on the
  hardware run.
- Cold-start auto-retry into Degraded (a failed `Start` currently just stays Stopped, error surfaced).
- Doc task: create `spike/PHASE0-RESULTS.md` with the measured Phase 0 numbers.
- **UI/UX pass for the WPF Board — mostly done, one piece remains.** WPF-UI Fluent chrome, `ui:Button`
  styling (incl. engine controls), design-09 dark tokens, colour-filled phrase tiles, and coloured tag
  chips all landed across the duck-slider/WPF-UI-polish and board-library-ui work. Still outstanding:
  the named **Full/Docked layouts** and the [design 05 §2](docs/design/05-ui-design.md) interaction
  states (hover/press/focus specifics beyond what WPF-UI gives for free) — pick this up alongside or
  after the setup-wizard UI.

## Open questions

The Phase 0 technical unknowns are now **resolved** by the passed gate (2026-06-15):

- ✅ **A5** — Zoho/Chrome respects the `CABLE Output` mic selection. *(confirmed on the real call)*
- ✅ **A6** — Chrome's **AGC** passes pre-recorded phrases intelligibly. *(confirmed; capture
  exact AGC/level notes in `spike/PHASE0-RESULTS.md`)*
- ✅ **A11** — mouth-to-Chrome latency is acceptable end-to-end. *(confirmed; record the
  measured number in `spike/PHASE0-RESULTS.md`)*
- ✅ The `SetDuckingPreference` opt-out holds across repeated call start/stop cycles.

## Deferred / blocked items

- 🔒 **Board design mockups** — run the gstack designer for 3 dark-theme Board variants
  (Full + Docked) against [design 09](docs/design/09-design-system.md). **Blocked on an OpenAI
  API key** (`~/.gstack/openai.json` or `OPENAI_API_KEY`). Best done before Phase 3 builds the
  Board in XAML. _(Carried over from the old TODOS.md, 2026-06-10.)_
- Post-MVP backlog lives in the [roadmap](docs/roadmaps/mvp-roadmap.md#deferred-post-mvp-backlog).
