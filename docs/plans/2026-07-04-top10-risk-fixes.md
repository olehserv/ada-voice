# Top 10 Risk Fixes — 2026-07-04

Source: `docs/reviews/2026-07-04-full-codebase-review.md` (Top 10 Risks). All findings were
re-verified against current source before this plan. Scope: C1, C2, H1, H2, H3, H4, H5, H7,
H8, H9, H10, plus three tiny riders in the same files (H6, M3, M11).

## Context

The review scored Reliability 5/10: the recovery paths hold the real bugs. These fixes make
the app survive its bad days — device faults, locked files, crashes — without silent data
loss or a dead UI.

## Fixes by group

### Group A — App startup hardening (`src/AdaVoice.App/App.xaml.cs`)
- **M11** Log path: `AppContext.BaseDirectory\logs` → `AdaVoicePaths.DefaultRoot\logs`
  (also in `src/AdaVoice.Host/Program.cs:13`). Install dir is not writable under Program Files.
- **H2** Register all three global handlers in `OnStartup`:
  `DispatcherUnhandledException` (log Fatal + message + `Handled = true`),
  `AppDomain.UnhandledException` (log Fatal + flush), `TaskScheduler.UnobservedTaskException`
  (log Error + `SetObserved`).
- **H3** Crash restart: make `AdaVoice.Host/NativeMethods.cs` public; call
  `RegisterApplicationRestart(null, 0)` in `App.OnStartup` (mirrors `Program.cs:11`).
- **H4** Single-instance guard: named `Mutex("Local\\AdaVoice.SingleInstance")` at top of
  `OnStartup`; on conflict show a message and `Shutdown()`. Release in `OnExit`.

### Group B — UI thread / error surfacing (`src/AdaVoice.App/ViewModels/BoardViewModel.cs`)
- **H5** `PreviewTake`: sync `RelayCommand` blocking the UI for take length + 1 s →
  `async Task` + `await Task.Run(...)`, mirroring `TestOnHeadphones` (lines 265–277).
  Set the notice before playback starts. XAML binding is safe (`IAsyncRelayCommand` is
  `ICommand`; no CanExecute/parameter involved).
- **H6** Broad `catch (Exception ex) when (ex is not OutOfMemoryException)` → `Notice` in
  both `TestOnHeadphones` and the new `PreviewTake` (pattern from
  `CalibrationStepViewModel.cs:40-57`).
- Update `BoardViewModelTests.Preview_take_...` to `await ExecuteAsync(null)` like the
  existing `TestOnHeadphones` tests.

### Group C — Host seam error flow (H7)
Widen `IPlaybackHost.StateChanged` from `EventHandler<EngineState>` to
`EventHandler<EngineStateChangedEventArgs>` carrying `(EngineState State, string? Error)`.
- `src/AdaVoice.Host/IPlaybackHost.cs:16` — new args type + event.
- `src/AdaVoice.Host/EngineHost.cs:183,477-478` — pass `s.Error` through (already in hand).
- `src/AdaVoice.App/ViewModels/StatusViewModel.cs` — handler signature + new
  `StateError` observable property.
- `src/AdaVoice.App/MainWindow.xaml` status bar — new TextBlock bound to `Status.StateError`.
- `tests/AdaVoice.App.Tests/FakePlaybackHost.cs:85-89` — `RaiseStateChanged(state, error = null)`.
Blast radius verified: StatusViewModel is the only subscriber; FakePlaybackHost the only fake.

### Group D — Audio engine recovery (`src/AdaVoice.Audio`)
- **C1** Mic duck lost after rebuild: new `MicDuckRelay : IMicDuck` owned by the engine;
  forwards `Duck()` to the current passthrough and remembers the last command.
  `BuildGraph` hands the relay to `PhrasePlayer`; `RebuildMic` retargets the relay, which
  re-applies the last duck to the new passthrough (covers rebuild-mid-phrase).
  Regression test at engine level: play → mic fault → rebuild → assert new passthrough ducked.
- **H1** `AttemptRebuild` (AudioEngine.cs:229-257): add `catch (Exception)` treating unknown
  errors as transient-with-backoff (raise `RebuildResult(false)`, advance `_attempt`/
  `_nextAttemptMs`). Add `CableFormat` to `FakeDeviceFactory` so a wrong-rate cable can be
  armed; add the test that fails today (`NotSupportedException` from `Init` on rebuild).
  Factory-side COM translation (review rec. b) deferred to the WASAPI hygiene pass.
- **H10** Drift on audio threads: new `EngineCommand.DriftNoticed(DriftKind)`; `OnDrift`
  posts it instead of raising; control-thread handler raises `EngineEvent.DriftLogged`.
  Mirrors the existing `StreamFaulted` post pattern (AudioEngine.cs:381-391).
- **M3** rider: `Recorder.cs:79` duration math `* 1000` → `* 1000L`.

### Group E — Core storage safety (`src/AdaVoice.Core`)
- **C2** ReadError overwrite: guard all six mutator saves (`Add:114`, `Delete:134`,
  `AddCategory:155`, `UpdateCategory:169`, `DeleteCategory:190`, `EditPhrase:241`) on
  `LoadStatus != ReadError`, same rule the tag-migration save already follows (line 63).
  Guard = `EnsureWritable()` throwing `InvalidOperationException` with a clear message.
  Surface at startup: `ILibraryHost` gains `string? LibraryWarning`; `EngineHost` maps
  ReadError/Corrupt/RecoveredFromBackup to operator text; `BoardViewModel` seeds `Notice`.
  Tests: new fake `IPhraseRepository` returning `ReadError`; assert mutators refuse and
  `Save` is never called.
- **H9** Import overwrites WAVs: re-key imported audio — extract archive entry
  `audio/{originalFileName}` to `{phrase.Id}.wav` and rewrite `FileName` for added phrases
  (both Merge and Replace). Export unchanged (old archives keep working). Test: merge an
  archive whose new-id phrase reuses an existing local file name; assert local WAV intact.

### Group F — Interop correctness
- **H8** `DuckingOptOut.cs`: add `[PreserveSig]` to all 20 int-returning methods across the
  four `[ComImport]` interfaces (lines 58–101). No other change; call sites already use
  `Marshal.ThrowExceptionForHR`.

## Order of work
A (isolated) → F + M3 (one-liners) → B → C → E → D (largest, engine tests last).

## Verification
- `dotnet build` clean; `dotnet test` (302 existing tests) green plus new tests:
  C1 rebuild-duck, H1 wrong-rate-cable rebuild, C2 ReadError refusal, H9 merge collision,
  H5 async preview, H7 error pass-through.
- Manual smoke (user): launch twice → second instance exits with message; preview a pending
  take → UI stays responsive; logs appear under `%LOCALAPPDATA%\AdaVoice\logs`.
