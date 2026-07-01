# Setup Wizard UI — Design Spec

_Date: 2026-07-02. Status: approved (brainstorming). Next: implementation plan._

## Problem

Design 05 §4 specifies a 9-step setup wizard (VB-CABLE detection, environment checks, device
selection, voice calibration, hotkey check, Chrome/Zoho instructions, loopback self-test, latency
note, first-call confidence card). The underlying logic for several steps already exists and is
unit-tested (`EnvironmentChecks`, `VoiceCalibration`, `EngineHost.Calibrate`/`RunEnvironmentChecks`,
`HotkeyService`) but there is no UI that runs the operator through it. Today she has no guided way
to confirm her setup before a real client call.

## Scope

The full 9-step design splits into two very different buckets of work: steps that reuse
already-working, already-tested logic ("Bucket A"), and steps that need genuinely new capability
— live level meters (none exist anywhere in the app today), a loopback self-test (the audio layer
only *renders to* the cable, never *captures from* it), VB-CABLE install detection, three
additional OS-setting checks (mic privacy, Volume Mixer mute, Communications default), and device
selection (the engine has no seam to change which physical mic/cable it uses at runtime — that's
fixed at construction). Building all nine in one pass would mean designing new audio capability and
a UI feature at the same time.

**In this slice (Bucket A):**
1. **Environment checks** — the 4 that already exist (cable present, cable at 48 kHz, default
   output ≠ cable, mic present), via the existing `EnvironmentChecks`/`RunEnvironmentChecks`. When
   the cable check fails, show a link to the VB-CABLE download page (the only piece of "VB-CABLE
   detection" that fits Bucket A — presence is already what the cable check tests).
2. **Voice calibration** — wraps the existing `EngineHost.Calibrate(5)` (records 5 s, measures RMS,
   persists `MicReferenceRms`) with a background-thread call and a cosmetic countdown.
3. **Hotkey status** — reports the result of the stop-hotkey registration that already happens
   automatically in `MainWindow.OnLoaded` (no new "live press-to-test" step; informational only).
4. **Instructions** — text-only steps for setting Chrome/Zoho's microphone to CABLE Output. No
   screenshots in this slice (real screenshots can be dropped in as image assets later without
   changing the step's structure).
5. **First-call confidence card** — a 3-item checklist nudging her to make a real test call before
   trusting the app on a client call. Local UI state only, not persisted.

Triggered on first run (auto-shown, modal, owned by the already-visible `MainWindow`) and
re-runnable at any time via a "Setup…" button in the Board's status bar.

**Out of scope for this slice (tracked as a v2 follow-up):**
- Live level meters (calibration, device picker, Settings routing).
- Loopback self-test (speak → confirm signal on the cable; play a test tone → confirm).
- Device selection (choosing among multiple mics/cables/monitors) — needs new settings fields and
  an `EngineHost` re-init path that doesn't exist today.
- The 3 additional environment checks (mic privacy, Volume Mixer mute, Communications = "Do
  nothing") — need new Windows-setting probes, not audio-device probes.
- VB-CABLE control-panel latency note (pure copy — cheap, but bundled with the rest of the
  "advanced" instructions content pass; not required for the wizard to be useful).

## Architecture

WPF concern (the wizard shell, its steps, and their view-models), backed by one new host seam so
the step view-models stay unit-testable with a fake, exactly like every other Board view-model.

```
AdaVoice.Host
└── ISetupHost.cs           seam: RunEnvironmentChecks(); Calibrate(seconds)
    (EngineHost already implements both methods — just add the interface)

AdaVoice.App
├── SetupWizardWindow.xaml(.cs)      shell: ContentControl + DataTemplate per step type,
│                                    Next/Back/Skip anyway/Finish buttons
├── ViewModels/
│   ├── SetupWizardViewModel.cs      orchestrates step order, gating, DialogResult on Finish
│   ├── EnvironmentChecksStepViewModel.cs
│   ├── CalibrationStepViewModel.cs
│   ├── HotkeyStatusStepViewModel.cs
│   ├── InstructionStepViewModel.cs
│   └── FirstCallStepViewModel.cs
└── App.xaml.cs (composition)   shows MainWindow, then the wizard (owned by it) if not
                                 WizardCompleted; BoardViewModel gets a re-run entry point
```

### Components

- **`ISetupHost`** — the narrow seam the wizard needs: `IReadOnlyList<EnvironmentCheck>
  RunEnvironmentChecks()`, `CalibrationResult Calibrate(int seconds = 5)`. `EngineHost` already has
  both methods with matching signatures. `FakePlaybackHost` gains it too, mirroring the existing
  convention of one fake implementing every seam (it already mirrors `EngineHost` implementing
  every seam on one object).
- **`IWizardStep`** — the shared per-step contract: `bool CanAdvance { get; }`. Content-only steps
  (instructions, first-call) always return true; gated steps (checks, calibration) compute it.
- **`SetupWizardViewModel`** — an ordered list of `IWizardStep`s, a `CurrentStepIndex`, and
  `Next`/`Back`/`SkipAnyway`/`Finish` commands. `Next` is enabled only when the current step's
  `CanAdvance` is true; `SkipAnyway` is offered whenever it isn't. `Finish` (only reachable from the
  last step) is what signals real completion — see Data flow.
- **`EnvironmentChecksStepViewModel(ISetupHost)`** — `Checks`, `AllPassed` → `CanAdvance`; a
  `Recheck` command re-runs `RunEnvironmentChecks()` (cheap, synchronous, no threading needed).
- **`CalibrationStepViewModel(ISetupHost)`** — `IsRecording`, `Result` (nullable
  `CalibrationResult`); `StartCalibration` is an async command running `Task.Run(() =>
  setup.Calibrate(5))` — the same background-thread pattern as `BoardViewModel.TestOnHeadphones`,
  since `Calibrate` blocks synchronously for the recording duration. `CanAdvance => Result is {
  Ok: true }`, but also offers `SkipAnyway` (see Error handling — calibration is not a hard gate,
  same as the checks step). The View owns a purely cosmetic 5-second countdown ring animation
  triggered by `IsRecording`; the view-model does not track seconds.
- **`HotkeyStatusStepViewModel(string? activeHotkey)`** — formats the already-resolved hotkey label
  ("Pause" / "Ctrl+F12" / "not available — use the on-screen STOP button"). `CanAdvance => true`
  always; a missing hotkey is informational, not a blocker.
- **`InstructionStepViewModel`** — static numbered text content. `CanAdvance => true`.
- **`FirstCallStepViewModel`** — a local (non-persisted) 3-item checklist. `CanAdvance => true`; its
  Next button reads "Finish."
- **`Settings.WizardCompleted`** (bool, default false) + `ISettingsHost.WizardCompleted { get; }` /
  `MarkWizardCompleted()` — same shape as the `WindowPlacement` addition from the Board library UI
  round 2.

### Data flow

**First run.** `App.xaml.cs` builds `_host`/`status`/`settings` and `MainWindow` exactly as today,
calls `window.Show()` (so `OnLoaded` registers the hotkey and `window.ActiveHotkey` is resolved),
then:

```csharp
if (!settings.WizardCompleted)
{
    var wizard = new SetupWizardViewModel(_host, window.ActiveHotkey);
    if (new SetupWizardWindow { DataContext = wizard, Owner = window }.ShowDialog() == true)
        settings.MarkWizardCompleted();
}
```

The Board is already visible underneath the modal wizard — matches design 05's `Wizard → Board`
screen diagram, and means closing the wizard early leaves her looking at a real (if unconfigured)
Board, not a blank screen.

**Step sequencing.** Linear: Environment checks → Calibration → Hotkey status → Instructions →
First-call card. `Back` is always allowed (no re-validation needed going backward). `Next`/`Finish`
are gated per-step by `CanAdvance`.

**Completion signal.** Only reaching `Finish` on the last step sets `DialogResult = true` (the same
convention `PhraseEditDialog`'s Save uses) — a mid-wizard close (X, Alt+F4, Cancel) leaves it
`false`/`null`, so `WizardCompleted` is never set and the wizard reappears next launch.

**Re-run entry point.** A "Setup…" button in the Board's status bar (next to the engine controls,
not the phrase-library row). `BoardViewModel.RunSetupCommand` builds a **fresh**
`SetupWizardViewModel` each time (no stale results from a previous run) via an injected
`showSetupWizard` callback, following the same constructor-callback pattern as
`showManageCategories`. Re-completion is a harmless no-op re-write of `WizardCompleted`.

**Persistence boundary.** Only `WizardCompleted` and (inside calibration) `MicReferenceRms` are
ever written to `settings.json`. Environment checks and hotkey status are always read fresh, never
cached.

## Boundaries / dependencies

- `App → Host → Core` (unchanged direction). Step view-models depend only on `ISetupHost` (or take
  a plain string, for the hotkey step) — never on the concrete `EngineHost` — so they are
  unit-testable with a fake, matching every other Board view-model.
- No changes to the audio engine or the recording/playback path. `Calibrate` and
  `RunEnvironmentChecks` are pre-existing, already-tested methods; this slice only adds a UI that
  calls them.
- `BoardViewModel`'s constructor gains one more optional callback parameter
  (`showSetupWizard`), continuing its existing (already long) dialog-callback pattern rather than
  fixing it. Flagged as a future cleanup candidate (e.g. bundling the dialog callbacks into one
  small interface) — out of scope for this slice.

## Error handling

- **Failed environment check:** `Next` stays disabled while any check fails; `SkipAnyway` is always
  available and advances regardless — the button itself is the conscious choice (design 05).
- **Calibration too quiet:** `Calibrate` already returns `Ok: false` with a retry message
  (`VoiceCalibration.FromTrimmedSamples`) — existing, tested logic. The step shows the message and
  re-enables `StartCalibration`.
- **Calibration can also be skipped:** `Settings.MicReferenceRms` is already nullable-means-
  uncalibrated (the recorder has a dBFS fallback for exactly this case), so this step offers
  `SkipAnyway` too, for consistency with the checks step — she is never stuck if her mic is
  genuinely unusable that day.
- **Hotkey unavailable:** never blocks (`CanAdvance => true` always); the on-screen STOP is the
  real fallback and already works today regardless of the wizard.
- **Window closed without finishing** (X, Alt+F4, Cancel): `DialogResult` stays falsy, so
  `WizardCompleted` is never set — no partial-progress persistence to reason about, since nothing
  is written until `Finish`.
- **`RunEnvironmentChecks` / `Calibrate` throwing:** both are already defensive
  (`WasapiEnvironmentProbe` catches per-device failures; `Calibrate` only writes settings on
  `result.Ok`) — no new try/catch needed at the wizard layer.

## Testing

- **Unit (CI), with a fake `ISetupHost`** (extending `FakePlaybackHost`, matching its existing
  convention): every step view-model's `CanAdvance` transitions; `EnvironmentChecksStepViewModel
  .Recheck`; `CalibrationStepViewModel.StartCalibration` (success, too-quiet retry, skip);
  `HotkeyStatusStepViewModel`'s label formatting (set / null); `SetupWizardViewModel`'s
  Next/Back/Skip/Finish sequencing and its "only completed on real Finish" `DialogResult` contract.
- **Settings round-trip:** `WizardCompleted` default-false and persist-true, same pattern as the
  existing `JsonSettingsRepositoryTests`.
- **Not unit-testable — needs manual smoke** (same honesty this project has applied to every prior
  UI slice): the window actually opening on first run, the countdown ring animation, the
  `DataTemplate` step-switching rendering the right view per step, and the re-run "Setup…" button.
  The implementation plan will call this out explicitly rather than let a green test count imply
  the dialogs work.

## Open decisions (resolved this session)

- **Device selection deferred to v2** — it needs new engine capability (settings fields + an
  `EngineHost` re-init path), not just UI; bundling it into v1 would double the slice's size.
- **No screenshots in the instruction step for v1** — text-only; real screenshots can be added
  later as assets without restructuring the step.
- **Failed checks and quiet calibration are both skippable**, matching design 05's "skip anyway"
  policy rather than hard-blocking either.
