# AdaVoice — Handoff & Progress

**Live status of the project.** Read this first when you (or a new session) pick the work
back up. It answers one question: *where are we right now?*

- **What it is:** done work, work in progress, anything interrupted, and open questions.
- **What it is not:** the plan (see [implementation plan](docs/plans/implementation-plan.md)),
  the strategy (see [roadmap](docs/roadmaps/mvp-roadmap.md)), or the decision record
  (canonical table in [design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).

_Last updated: 2026-06-22._

---

## Status in one line

**Design complete and reviewed. Phase 0 go/no-go gate PASSED on the target machine
(Architecture A confirmed). Phase 1 engine runs live end-to-end: engine core + WASAPI
factory/monitor + a `SystemEngineClock` and a console composition root (`AdaVoice.Host`).** Next
real step: run it on the target machine (hardware checklist below), then the `Recorder`.

## Done

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
- ✅ **Doc structure cleanup** (2026-06-13) — removed the original brief (`1_DESIGN.md`) and
  `TODOS.md`; moved the design system into [`docs/design/09-design-system.md`](docs/design/09-design-system.md);
  added planning docs under [`docs/plans/`](docs/plans/).

## In progress / interrupted

- _Nothing actively in progress._ The project is paused inside Phase 1: the engine runs live in
  code, but the host has not yet been run on the target machine, and the `Recorder` is not built.

## Next action

**1) Run the host on the target machine** (the engine has never touched real hardware as a whole):
`dotnet run --project src/AdaVoice.Host`, then `S` (Live, mic audible on `CABLE Output`), `P` (beep
to the cable), `O`/`O` (OFF AIR off/on), unplug the cable (alarm sounds + rebuild logged), replug
(fast recovery), `Q`. Watch the Serilog file under the host's `logs/`. Note: the cable must be at
48 kHz or `Start` will surface a format error and stay Stopped (by design for now).

**2) Then build the `Recorder`** (trim + RMS loudness-match + preview + save) that drives OFF AIR.

Then the WPF UI / ViewModels (Phase 3) reuse `EngineHost`. Smaller doc task still open: create
`spike/PHASE0-RESULTS.md` with the measured Phase 0 numbers. Deferred from this slice: cold-start
auto-retry into Degraded (a failed `Start` currently just stays Stopped with the error surfaced).

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
