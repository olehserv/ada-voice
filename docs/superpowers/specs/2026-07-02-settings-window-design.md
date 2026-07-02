# Settings Window — Design Spec

_Date: 2026-07-02. Status: approved (brainstorming). Next: implementation plan._

## Problem

Design 05 §4 specifies a grouped Settings IA (Levels → Behavior → Language & Backup → Devices,
frequent first, dangerous last), but no Settings window exists yet. Today there is exactly one
inline control: a mic-duck slider built directly into the Board's status bar
(`SettingsViewModel`/`ISettingsHost`), built before Settings existed as its own screen. Everything
else in design 05's Settings — behavior toggles, language choice, device pickers, backup/export —
has no UI at all. This is slice 1 of
[`docs/plans/ui-ux-localization-scope.md`](../../plans/ui-ux-localization-scope.md).

## Scope

Design 05's Settings IA has four groups. This slice builds three of them; the fourth needs new
audio capability first and is a separate, later slice.

**In this slice:**
1. **Levels** — the mic-duck slider (moved here from the Board's status bar) and a "re-run voice
   calibration" control reusing the wizard's existing `CalibrationStepViewModel`/`CalibrationStepView`.
2. **Behavior** — board always-on-top toggle (live), a "new trigger stops the current phrase"
   toggle (maps to the existing `PhrasePlayer.ReplaceOnRetrigger` option, applies on restart), and
   a read-only display of the currently active stop hotkey (no reassignment — see below).
3. **Language & Backup** — a language picker (English / Українська / Polski; persists and offers
   an immediate restart; does not yet change any displayed text — the `.resx` retrofit is slice 4),
   manual export/import wired to the existing `LibraryArchiveService`, a last-backup-date readout,
   and an "open backup folder" action.

**Deferred out of this slice (with reasons):**
- **Devices group** (mic/cable/monitor pickers with live level meters) — needs real new capability
  that doesn't exist anywhere in the app today: there is no live audio level metering, no
  `Monitor` device role in the engine (preview currently plays to the system default output
  directly), and device names aren't wired to `settings.json` at all (`WasapiAudioOptions` is
  constructed with hardcoded defaults in `App.xaml.cs`). This is an audio-engine capability slice,
  not a Settings-screen wiring job — own future slice.
- **Phrase monitor slider** (design 05 §4 lists it alongside the duck slider in Levels) — deferred
  for the same reason as the Devices group, and bundled with it: `Settings` has
  `MonitorDeviceName`/`MonitorEnabled` but no monitor *level*, and `Preview()` only ever applies
  the phrase's own `gainDb` — there's no live-level seam to hook a slider to. A monitor-level
  control without a monitor-device picker is half a feature anyway, so it ships together with
  Devices, not in this slice.
- **True hotkey reassignment** (design 05: pick any key, live press-to-test, conflict surfaced) —
  `HotkeyService` today only ever tries two fixed candidates (`Pause`, then `Ctrl+F12`); there is no
  capture-any-key mechanism. Building one is a real, separate feature. This slice only shows which
  of the two fixed candidates is currently active.
- **Live-apply for the retrigger toggle** — `PhrasePlayer.ReplaceOnRetrigger` is set once at
  construction with no live-mutation path today (unlike the duck level, which already has one via
  `SetDuckLevel`). Adding one is possible but is a new engine seam; this slice reads the setting
  once at startup instead, same pattern as `WizardCompleted`.

**Deliberate trade-off:** the duck slider moves out of the Board's status bar entirely (matches
design 05's original mockup, which shows it inside Settings, not inline on the Board) rather than
staying inline plus being duplicated in Settings. This is a conscious UX change from what's shipped
today — mid-call duck adjustment now requires opening Settings instead of one glance at the status
bar. Accepted because it matches the reviewed design and keeps one source of truth for the control
instead of two bound views of the same value.

## Architecture

### Trigger

A `Settings…` button next to the existing `Setup…` button in the Board's status bar
(`MainWindow.xaml`). `BoardViewModel` gets a `RunSettingsCommand` that calls an injected
`Action<SettingsWindowViewModel> _showSettings` delegate — the same delegate-injection pattern
already used for `_showSetupWizard`. `MainWindow` gets a `ShowSettings(SettingsWindowViewModel vm)`
method mirroring `ShowSetupWizard`, which constructs and shows a new modal `SettingsWindow`.

### ViewModels (new, under `src/AdaVoice.App/ViewModels/`)

- **`SettingsWindowViewModel`** — thin composition root exposing `Levels`, `Behavior`, `Backup`
  sub-view-models. Owns nothing itself.
- **`LevelsSettingsViewModel`** — `MicDuckDb`/`DuckLabel` (moved from `SettingsViewModel`) plus a
  `Calibration` property holding a `CalibrationStepViewModel` instance (reused unmodified from the
  wizard — its only wizard-specific member, `CanAdvance`, is simply unused here).
- **`BehaviorSettingsViewModel`** — `AlwaysOnTop` (bool, live + persisted), `ReplaceOnRetrigger`
  (bool, persisted only, restart-to-apply), `ActiveHotkey` (string?, read-only, set once from a
  `Func<string?>` constructor delegate — the same one `MainWindow.ActiveHotkey` already feeds to
  `BoardViewModel`).
- **`BackupSettingsViewModel`** — `Language` (persisted, restart offered on change), `ExportCommand`
  / `ImportCommand`, `LastBackupDate`, `OpenBackupFolderCommand`. File-picker dialogs and the
  Merge/Replace choice are owned by the window's code-behind via injected delegates
  (`Func<string?> pickExportPath`, `Func<(string Path, ImportMode Mode)?> pickImportFile`,
  `Func<bool> confirmRestart`, `Action<string> showError`) — the same delegate-for-UI-concern
  pattern `BoardViewModel` already uses for `confirmDelete`/`showEditDialog`, keeping every VM here
  WPF-free and unit-testable with fakes.

`SettingsViewModel` (existing) keeps only `WindowPlacement` and `WizardCompleted` after the duck
slider moves out — those are Board/App-lifecycle concerns, not Settings-screen concerns, so they
don't belong in the new window's view-models.

### Host seam

`ISettingsHost` (implemented by `EngineHost`, same as today) gains:

```
bool AlwaysOnTop { get; }
void SetAlwaysOnTop(bool value);       // applies live + updates in-memory; SaveSettings() persists

bool ReplaceOnRetrigger { get; }
void SetReplaceOnRetrigger(bool value); // in-memory + SaveSettings(); read once at PhrasePlayer construction

string Language { get; }
void SetLanguage(string code);          // in-memory + SaveSettings(); no live text change in this slice

void Export(string destinationZipPath);              // delegates to the existing ExportLibrary
ImportResult Import(string sourceZipPath, ImportMode mode); // delegates to the existing ImportLibrary

DateOnly? LastBackupDate { get; }       // new small public method on BackupService
```

No new host interface. Settings stays one feature area behind one seam, consistent with
`ILibraryHost` / `IPlaybackHost` / `IRecorderHost` / `ISetupHost` each already owning one area.

**Export/Import already exist on `EngineHost`** — `ExportLibrary(string)` and
`ImportLibrary(string, ImportMode)` (used today by the console host's manual commands) already
delegate to `LibraryArchiveService`, and `ImportLibrary` already calls `_library.Reload()` on
success. None of that backend work is new; it is simply not declared on any interface yet, so no
WPF code can reach it. This slice only adds `Export`/`Import` to `ISettingsHost` and forwards to
the methods that already exist — same shape as Task 2 of the setup-wizard plan, where
`RunEnvironmentChecks`/`Calibrate` already existed on `EngineHost` and only needed the interface.

**Defaults for existing installs:** the user's real machine already has a `settings.json` from
actual use. `AlwaysOnTop` defaults to `true` (matches today's hardcoded `Topmost="True"` exactly —
no behavior change for anyone until they explicitly turn it off). `ReplaceOnRetrigger` defaults to
`true` (matches `PhrasePlayerOptions.ReplaceOnRetrigger`'s current default). `Language` defaults to
`"en"` (matches the app's current English-only UI). All three deserialize to these defaults when
absent from an older `settings.json`, so no migration step is needed.

## Data flow

**Levels** — Duck slider: unchanged live-apply-on-change + save-on-drag-end, now living in
`LevelsSettingsViewModel`. Calibration: `StartCalibration` → `Task.Run(ISetupHost.Calibrate(5))` →
`CalibrationResult`, same success/too-quiet copy already built for the wizard.

**Behavior** — `AlwaysOnTop` toggle applies immediately (two-way bound into `Window.Topmost`,
replacing today's hardcoded `Topmost="True"`) and saves immediately — a checkbox needs no
drag-end debounce, unlike the slider. `ReplaceOnRetrigger` toggle only persists; a static "applies
after restart" caption sits next to it. `ActiveHotkey` is set once at construction and never
changes during the window's lifetime.

**Backup** — Language selection persists, then immediately shows a restart-confirmation dialog
(`confirmRestart` delegate); confirming does `Process.Start` a new instance of the app followed by
`Application.Current.Shutdown()`. Export: `pickExportPath()` returns a path or null (cancelled) →
`ISettingsHost.Export(path)`. Import: `pickImportFile()` returns `(path, ImportMode)` or null →
`ISettingsHost.Import(path, mode)` → `ImportResult`; the success message tells the user a restart
is needed to see the results on the Board (see "Import refresh" decision below — `Import` does not
touch the running app's in-memory library, only disk). `LastBackupDate` is read once when the
window opens. `OpenBackupFolderCommand` shells out to Explorer at `AdaVoicePaths.BackupsDir(root)`.

**Import refresh — accepted limitation:** `EngineHost.ImportLibrary` already reloads its own
in-memory library on success, so the host-level data (`ILibraryHost.Phrases`, etc.) is correct
immediately, with no new code needed. The gap is one level up: `BoardViewModel.Phrases` is an
`ObservableCollection` mutated only at two specific points today (add-one on record-save, remove-one
on delete) — nothing re-pulls it from `ILibraryHost` after construction, so a reload on the host
side does not, by itself, appear on screen. A manual import can add many phrases/categories at
once; making that appear on the Board live would need a new `RefreshFromLibrary()`-style method on
`BoardViewModel`, which is out of scope for this slice. The user sees the imported phrases after the
next restart (a fresh `BoardViewModel` builds `Phrases` from the already-correct, already-reloaded
library); the success toast says so explicitly.

## Error handling

- **Calibration failure** — already handled today (the reused view-model catches non-OOM exceptions
  → "could not access the microphone" message).
- **Export failure** (disk full, permission denied, bad path) — `LibraryArchiveService.Export`
  throws; `BackupSettingsViewModel.ExportCommand` catches it and surfaces the message via the
  injected `showError` delegate. Never crashes the window.
- **Import failure** — expected failures (unreadable zip, missing/invalid `library.json`,
  unsupported schema version) already come back as a typed `ImportResult.Error` from the existing
  backend, not an exception; the view-model just displays it. Only a genuinely unexpected exception
  needs the same catch-all as Export.
- **Restart-now failure** — if `Process.Start` fails (e.g. a locked-down environment), catch it and
  close the dialog anyway; the setting is already saved on disk and takes effect on the next manual
  launch. A failed self-relaunch must never block the user from closing Settings.
- **Language / retrigger persistence** — rides the existing atomic-write `JsonSettingsRepository`;
  no new failure mode introduced.
- **Always-on-top / hotkey status** — no failure mode: one is an instant in-memory operation, the
  other is read-only.
- **Accepted limitation — concurrent settings writes during calibration:** `Calibrate()` saves
  `settings.json` from a background thread while it's recording; toggling Behavior/Language at the
  same moment saves it from the UI thread. Both go through `JsonSettingsRepository.Save`'s single
  fixed `settings.json.tmp`, so the two writes can race. This was already true before this slice,
  but calibration and the toggles lived on separate screens, making it nearly unreachable — this
  slice puts them in one window, making the race reachable in practice for the first time. Not
  fixed here (no locking added — out of scope for this slice); acceptable because the odds are low
  (a multi-second recording window) and the worst case is one write silently losing to the other,
  not data corruption (the atomic temp-then-rename write is preserved either way).

## Testing

- New view-models (`LevelsSettingsViewModel`, `BehaviorSettingsViewModel`,
  `BackupSettingsViewModel`) get unit tests against a fake `ISettingsHost`, in the same style as the
  existing `SettingsViewModel`/`CalibrationStepViewModel` tests: apply/save split for each toggle,
  picker-delegate wiring (fakes returning canned paths or null-for-cancelled), and
  `ImportResult.Error` surfacing without throwing.
- There is no dedicated test project for `AdaVoice.Host` today — `EngineHost` itself is never
  directly unit-tested anywhere in this repo; it's only exercised through hardware smoke tests and,
  indirectly, through the App-layer fakes (`FakeSettingsHost`, `FakePlaybackHost`) that stand in for
  it in view-model tests. This slice follows that exact precedent (same as the setup wizard's
  `WizardCompleted`/`ISetupHost` work): `AlwaysOnTop` / `ReplaceOnRetrigger` / `Language` get a
  Core-level round-trip test through `JsonSettingsRepository` (default value + persist + reload);
  `EngineHost`'s new `ISettingsHost` members are one-line delegations verified by a clean build, not
  a dedicated test.
- `BackupService` gets one new small Core-level unit test for the newest-backup-date lookup,
  alongside its existing backup tests.
- No new automated WPF/window-level tests — consistent with the rest of the app; the window
  itself gets manual smoke-testing, same as the setup wizard was.
- Manual smoke checklist (before merge): duck slider works from its new home; recalibrate succeeds
  and handles the too-quiet retry; always-on-top toggles live; the retrigger toggle's restart note
  is honest (toggling it does nothing until relaunch); language picker persists and "Restart now"
  relaunches correctly; export produces a real, valid zip; import (both Merge and Replace) against
  a real exported file round-trips correctly (verified after restart); open-backup-folder opens
  Explorer at the right path.
