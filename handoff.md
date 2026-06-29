# AdaVoice — Handoff & Progress

**Live status of the project.** Read this first when you (or a new session) pick the work
back up. It answers one question: *where are we right now?*

- **What it is:** done work, work in progress, anything interrupted, and open questions.
- **What it is not:** the plan (see [implementation plan](docs/plans/implementation-plan.md)),
  the strategy (see [roadmap](docs/roadmaps/mvp-roadmap.md)), or the decision record
  (canonical table in [design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).

_Last updated: 2026-06-29._

---

## Status in one line

**Design complete and reviewed. Phase 0 go/no-go gate PASSED. Phase 1 engine runs live end-to-end
(engine core + WASAPI factory/monitor + `SystemEngineClock` + console host); the Recorder records →
trims → loudness-matches → catalogues a take into `library.json`, and preview plays it to the
monitor.** Next real step: run it on the target machine (checklist below), then the rest of storage
(categories/delete/backups/settings) and the setup wizard, then the WPF UI.

## Done

- ✅ **Operator pilot prepared (2026-06-29)** — [`docs/plans/operator-pilot.md`](docs/plans/operator-pilot.md):
  a functional-smoke pilot script (record fresh live, real test call). The supervised pilot passed
  (user: "tested everything, works awesome").
- ✅ **Duck slider + WPF-UI polish — on branch `feat/duck-slider-wpfui-polish` (not yet merged), 2026-06-29.**
  - Live mic-duck slider: `PhrasePlayer.SetDuck` + `EngineCommand.SetDuckLevel` (engine re-applies the
    level after a Stop/Start rebuild) → `ISettingsHost` on `EngineHost` (apply-live vs save split) →
    `SettingsViewModel` + a snapped −40..0 dB slider in the status bar (saves on drag-end).
  - WPF-UI 4.3.0 Fluent dark theme adopted: `ui:Button` appearances (STOP=Danger, Record=Primary),
    shared `PhraseButtonStyle`, finished tokens (type scale, spacing, radius; Segoe UI Variable),
    first-run welcome `ui:Card`, and a save toast (`Snackbar`). 167 unit tests green; app smoke-launches.
  - **Still TODO on this work:** hardware run (the slider's audible duck + persistence need the real
    cable + a call); decide on **FluentWindow chrome** (deferred — risk to Topmost) and merge.
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
  a disposed queue. 54 unit tests green; full solution builds clean. **Not yet run on hardware** —
  see Next action.
- ✅ **Recorder core — complete (2026-06-23)** — DSP (`Loudness` RMS/peak, `SilenceTrim`
  −45 dBFS/150 ms, `LoudnessMatch.ComputeGainDb` with −3 dBFS ceiling), `WavFile.Save` (float →
  16-bit PCM, atomic temp→final), and `Recorder` (record from a capture device → engine-format via a
  shared `Dsp.EngineFormat` converter with push-drain + resampler tail-flush → trim →
  loudness-match). Wired into the host on `[R]` (OFF AIR → record → save WAV under `recordings/`).
  72 unit tests green incl. a 44.1 kHz-stereo resample test; a capture-thread race in the Recorder
  was found and fixed (lock). **Saved takes are the raw trimmed audio; `gainDb` is metadata that
  will be applied once playback + library land.** **Not yet run on hardware.**
- ✅ **Storage + preview (thin vertical) — complete (2026-06-23)** — new `AdaVoice.Core` project
  (net10.0, no audio dep): `Library`/`PhraseEntry`/`Category` domain, `IPhraseRepository` +
  `JsonPhraseRepository` (System.Text.Json, atomic tmp→rename, seeded default), `PhraseLibraryService`
  (WAV-first `Add`). `WavFile.Load` added (and `Save` now creates its dir). Host catalogues a take
  into `%LOCALAPPDATA%\AdaVoice\library.json` + `audio\p-….wav` on `[R]`, and `[V]` previews the last
  phrase to the default output (monitor stand-in) with `gainDb` applied — **refusing if the default
  output is the cable** (cardinal rule). 80 unit tests green. **Not yet run on hardware.**
- ✅ **Doc structure cleanup** (2026-06-13) — removed the original brief (`1_DESIGN.md`) and
  `TODOS.md`; moved the design system into [`docs/design/09-design-system.md`](docs/design/09-design-system.md);
  added planning docs under [`docs/plans/`](docs/plans/).

## In progress / interrupted

- _Nothing actively in progress._ The project is paused inside Phase 1: engine + Recorder + storage +
  preview run in code but have not been run on the target machine; the rest of storage and the wizard
  are next.

## Next action

**1) Run the full loop on the target machine.** `dotnet run --project src/AdaVoice.Host`: `S` (Live,
mic on `CABLE Output`), `P` (beep), `O`/`O` (OFF AIR), unplug/replug the cable (alarm + fast
recovery); **`R` … speak … `R`** → "Saved p-…"; confirm `%LOCALAPPDATA%\AdaVoice\library.json` has the
entry and `audio\p-….wav` exists; **`V`** → the take plays on the headphones/default output (not the
cable); restart → it logs the phrase reloaded. Also: set the Windows default playback to `CABLE Input`
and press `V` → preview is **refused** with a log line. Cable must be at 48 kHz or `Start` stays
Stopped with an error (by design for now).

**2) Then the rest of storage + the wizard.** Categories CRUD + tags, delete-as-orphan, daily
backups, `settings.json`, a configurable monitor device (+ `Monitor` role) so preview targets a
chosen headphone device instead of the default output, the kill-9 atomic-write test, and startup
validation of missing files. Then the setup wizard (calibrates `micReferenceRms`). Then the WPF UI
(Phase 3) reuses `EngineHost`.

**Open follow-ups (named so they're not lost):**
- **Configurable monitor device:** preview currently uses the default output as the monitor
  stand-in; a real monitor device selection comes with `settings.json`.
- **2nd-capture fallback:** the Recorder opens its own mic capture; if a driver refuses a second
  WASAPI capture client, the fallback is tapping the engine's existing capture. Watch for it on the
  hardware run.
- Cold-start auto-retry into Degraded (a failed `Start` currently just stays Stopped, error surfaced).
- Doc task: create `spike/PHASE0-RESULTS.md` with the measured Phase 0 numbers.
- **UI/UX pass for the WPF Board (deferred on purpose).** The Phase 3 Board (`src/AdaVoice.App`) is a
  functional walking-skeleton — it plays phrases to the call, has a big STOP, an engine-state line, and
  is Topmost — built with plain WPF + the [design 09](docs/design/09-design-system.md) dark tokens. The
  engine-control buttons are still unstyled WPF defaults, and layout/spacing/typography are minimal. A
  real UI/UX pass is needed later: adopt WPF-UI (Fluent chrome), the Full/Docked layouts, proper button
  styling, and the [design 05 §2](docs/design/05-ui-design.md) interaction states. _(User confirmed
  2026-06-25: keep it simple for now, polish the UI/UX later. Verified the skeleton renders and plays a
  take on the target machine.)_

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
