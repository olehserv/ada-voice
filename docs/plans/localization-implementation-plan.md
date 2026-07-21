# AdaVoice localization (en/uk/pl) — implementation plan & progress

Full plan for [ui-ux-localization-scope.md](ui-ux-localization-scope.md)'s slice 4. Owner
needs the app in Ukrainian for a family member's beta trial — see the beta-release session
that produced `v0.1.0-beta.1`. Read this file first if picking this up in a new session;
it has the decisions and gotchas a fresh session would otherwise have to rediscover.

## Status: Stages 1–3 done, Stages 4–7 not started

**Not committed yet** — this is all uncommitted working-tree state. Run `git status` before
doing anything else. New/modified files: every `.xaml`/`.xaml.cs` in `src/AdaVoice.App/`
except the wizard's `SetupWizardWindow.xaml.cs` (no strings there), `Converters.cs`,
`Services/DialogPrompts.cs`, ~10 files under `ViewModels/`, plus new
`src/AdaVoice.App/Resources/` (Strings.resx + Strings.uk.resx + Strings.pl.resx +
Strings.cs), new `tests/AdaVoice.App.Tests/StringsTests.cs` +
`TestCultureInitializer.cs`, a fix to `tests/AdaVoice.App.Tests/Screenshots/WpfAppFixture.cs`
(Stages 1–2), and Stage 3's Audio/Core/Host code-not-text refactor (see below).

**Verified**: `dotnet build AdaVoice.slnx` clean; full App suite 288/288 (both
`ADAVOICE_SCREENSHOTS=1` and default mode); Core 107 / Audio 98 / Wasapi 8 / Host 12 all
green. Both wizard screenshots (`wizard-1-environment-checks`, `wizard-2-calibration`)
visually spot-checked after Stage 3 — the new converter-driven text renders correctly, not
blank (the real risk of removing record members XAML binds directly, since a bad binding
fails silently at runtime, not at build time).

## Architecture (settled — do not rework, see "why" below)

- **`.resx` satellite resources + `{x:Static}` + restart-to-apply.** `Strings.resx` (English,
  neutral) + `Strings.uk.resx` + `Strings.pl.resx` under `src/AdaVoice.App/Resources/`.
  Satellite compilation (the `.resources`/`uk`/`pl` assemblies) works automatically via the
  SDK's default `EmbeddedResource` glob — no extra csproj config needed.
- **`Strings.cs` is hand-written, not VS-generated.** Confirmed empirically:
  `PublicResXFileCodeGenerator` (the `<Generator>` metadata VS sets on a `.resx`
  `EmbeddedResource` item) is a Visual Studio IDE "single file generator" feature — `dotnet
  build` from the CLI never invokes it. A CLI build compiles the `.resources`/satellite
  assemblies fine but produces no `Designer.cs`. So `Strings.cs` is a plain static class with
  one `public static string Key => Get("Key")` property per resource key, backed by a
  `ResourceManager` reading `CultureInfo.CurrentUICulture`. **When adding new keys**: add the
  `<data>` entry to `Strings.resx` (English value) and the matching property to `Strings.cs`
  by hand — there is no code-gen step to run.
- **Culture is set once, at startup**, in `App.xaml.cs`'s `ApplyLanguage(_host.Language)` —
  called right after `_host = new EngineHost(...)` (so `_host.Language` is available) and
  before any window/ViewModel is built. Matches the existing restart-to-apply language model
  (`Settings.Language`'s doc comment, `BackupSettingsViewModel.OnLanguageChanged`'s restart
  prompt) — no live-switching needed.
- **Key naming**: `Area_Key` (e.g. `Main_Record`, `Settings_Title`, `Recorder_Discard`).
  "Area" is per-file/per-ViewModel, not a shared `Common_` bucket — even ubiquitous words like
  "Cancel"/"Delete"/"Done" got separate per-area keys (`PhraseEdit_Cancel`,
  `ManageCategories_Delete`, `Settings_Done`, …), deliberately, to keep each file
  self-contained during extraction. Two *are* shared, since they're genuinely the same
  context-free concept used via one shared helper: `DialogPrompts_Cancel` / `DialogPrompts_Ok`
  (the default buttons in `Services/DialogPrompts.cs`). If Stage 4 translation reveals the
  ubiquitous ones should consolidate, that's a mechanical follow-up — the English values are
  already consistent copies of each other, so no meaning is lost by leaving them split.
- **Parameterized strings**: prefer WPF's `Binding.StringFormat={x:Static res:Strings.Key}` in
  XAML over adding computed ViewModel properties — zero ViewModel surface change. Used for
  every `<Run>`-split sentence in `MainWindow.xaml` (e.g. `Main_CategoryEmptyTitle`,
  `Main_SearchNoMatchTitle`). In C#, use `string.Format(Strings.Key, args)`. Resource values
  use standard `{0}`/`{1}` placeholders either way.
- **Explicitly not localized** (by design, not oversight): the app name "AdaVoice" everywhere
  it appears alone or as a window-title prefix; the three language names in the language
  picker itself (`English`/`Українська`/`Polski` — each language shows its own native
  self-name, never translated into the current UI language — see the comment in
  `SettingsWindow.xaml`); pure glyphs (✓ ✕ ↑ ↓ ▶ ■ ⚠); filenames
  (`adavoice-export-{date}.zip`); numeric+unit labels with no real translatable content (e.g.
  `"{0:F0} dB"`, `"{0:0.0} s"` duration labels) — consistent with a pre-existing unextracted
  pattern already in the codebase.

## A real pre-existing bug found and fixed along the way

`WpfAppFixture` (the screenshot-test harness's shared WPF `Application`/dispatcher) claimed in
its own doc comment that `OnStartup` "never fires" since it never calls `Application.Run()`.
That's false — confirmed empirically by instrumenting `OnStartup` with a counter: merely
running `Dispatcher.Run()` on the thread that constructed the `Application` is enough for WPF
to raise `Startup` anyway. This means every screenshot/layout test run has always built a real
`EngineHost` against the real `%LOCALAPPDATA%\AdaVoice` data — reading settings.json/library.json,
and the constructor's daily-backup logic writing into `backups\`. It's been harmless in
practice (and is also *why* WPF-UI's pack:// resource loading has always worked in tests —
guarding `OnStartup`'s whole body off broke 27 tests with `NotSupportedException: The URI
prefix is not recognized`, confirming that hidden dependency). Fixed **narrowly**: a new
`App.SkipLanguageForTests` flag gates only the `ApplyLanguage` call (which would otherwise
overwrite the fixture's English culture pin with whatever language the real settings.json
has — e.g. "uk"). Nothing else about `OnStartup` changed. The broader isolation gap (tests
touching real user data) is real but out of scope here — logged in `handoff.md`'s open
follow-ups for a dedicated pass.

## Remaining work (Stages 4–7 of the original 7-stage plan)

### Stage 3 — Decouple Audio/Core display text into codes — done

`AdaVoice.Audio`/`AdaVoice.Core`/`AdaVoice.Host` no longer hold any English UI text (per
CLAUDE.md, those layers carry no UI concerns) — they return stable codes + structured data;
the App layer maps code → localized string. Every item from the original plan shipped, plus
two more of the same bug class found while implementing (see below).

- **`EnvironmentCheck`** (`src/AdaVoice.Audio/Setup/EnvironmentChecks.cs`) — new
  `EnvironmentCheckKind` enum (`CableOutput`/`CableSampleRate`/`DefaultOutput`/`Microphone`)
  replaces the old `Name`/`Detail` strings; the record now carries `RequestedName`/
  `FoundName`/`MeasuredSampleRate` (only the ones relevant to that check/status are set). App:
  two new `Converters.cs` converters (`EnvironmentCheckToTitleConverter`/
  `...ToDetailConverter`) map `Kind`+params → localized text via new `Strings` keys;
  `FailedCableCheckToVisibilityConverter` now matches `Kind` instead of an English literal.
  `EnvironmentChecksStepView.xaml`'s `{Binding Name}`/`{Binding Detail}` now go through those
  converters. Tests assert on `Kind`, never English — the litmus that the decoupling actually
  worked (a second-opinion review's suggestion, confirmed by making the change: nothing else
  needed the string).
- **`VoiceCalibration`** (`src/AdaVoice.Audio/Setup/VoiceCalibration.cs`) — new
  `CalibrationFailureReason` enum (`TooQuiet`/`RecordingInProgress`/`CouldNotPauseCallFeed`)
  replaces `CalibrationResult.Message`. The latter two reasons are host-level preconditions,
  not measurement outcomes (see the `EngineHost.Calibrate` finding below) — they share the
  enum since they're just other ways the same result can fail. App:
  `CalibrationStepViewModel` gained a computed `Message` property (`Result.Reason` → localized
  text, or a separately-tracked mic-access-exception message — that path is an App/UI concern,
  not one of Audio's codes) with its own `[NotifyPropertyChangedFor]` wiring;
  `CalibrationStepView.xaml`'s binding moved from `{Binding Result.Message}` to
  `{Binding Message}`.
- **`LibraryArchiveService.ImportResult`** (`src/AdaVoice.Core/Storage/LibraryArchiveService.cs`)
  — new `ImportErrorCode` enum (6 variants: open-failed, too-many-entries, json-too-large,
  no-valid-json, unsupported-version, import-failed) + structured params
  (`ExceptionMessage`/`EntryCount`/`FoundVersion`/`ExpectedVersion`) replace the old `Error`
  string. The framework `ex.Message` on the open/import-failed variants stays English by
  design — a system detail, not our own text. App:
  `BackupSettingsViewModel.DescribeError` maps code+params → localized text before formatting
  it into the existing `Backup_ImportErrorFormat` wrapper (composition unchanged from before —
  no new redundancy introduced).
- **"Uncategorized" display** — the stored `Category` name is untouched (it's persisted data
  other phrases reference by id — a live constraint, not a nice-to-have); only the *display*
  is localized, via a new `CategoryDisplay.NameOf(Category)` helper (`Converters.cs`) routed
  through every site that shows a category name: `PhraseEditDialog`'s category `ComboBox` (new
  `CategoryNameConverter`), `BoardViewModel`'s two filter-label properties,
  `MainWindow.xaml.cs`'s category filter menu, and `ManageCategoriesDialog`'s default row.
  **The last one needed real care, caught by a second-opinion review before writing code**:
  that row's name `TextBox` was two-way bound (`UpdateSourceTrigger=PropertyChanged`,
  auto-persisting on blur) with *no* guard against editing the default row at all — showing a
  localized label there and typing anywhere else in the dialog (any focus change commits)
  would have silently overwritten the stored "Uncategorized" with the localized string on the
  very first blur. Fixed with `CategoryRowViewModel.DisplayName` (localized get, no-op set for
  the default row) bound instead of `Name`, plus the `TextBox` disabled via a `Style`
  `DataTrigger` on `IsDefault` — screenshot-verified the row now renders visibly disabled
  (grayed), which is a legitimate, intentional visual change: the field was never supposed to
  be editable, and now it visibly isn't.

**Two more English leaks found beyond the original plan, same bug class, fixed the same way**
(the plan named `PhraseLibraryService` for the "read-only banner," but the actual assembled
text lived one layer up, in Host):
- **`EngineHost.LibraryWarning`** formatted `PhraseLibraryService.LoadStatus` into English
  text *in the Host layer* — `LibraryLoadStatus` was already the perfect code, just never
  exposed as one. `ILibraryHost.LibraryWarning` (`string?`) → `LoadStatus`
  (`LibraryLoadStatus`); `BoardViewModel` gained `LibraryWarningFor(LibraryLoadStatus)`
  mapping to the same three `Strings` keys the old switch used.
- **`EngineHost`'s settings-reset warning** — same shape, one field over:
  `_settingsWarning` was a hardcoded English string built from a bool
  (`JsonSettingsRepository.LoadReplacedCorruptFile`). `ISettingsHost.SettingsWarning`
  (`string?`) → `SettingsWereReset` (`bool`); `BoardViewModel`'s existing notice-fallback
  expression now maps it to `Strings.Board_SettingsWereReset`.
- **`src/AdaVoice.Host/Program.cs`** (the dev CLI harness, not shipped UI) needed updating to
  compile against the new shapes — kept intentionally as raw English/enum dumps
  (`$"{check.Kind}: requested=..., found=..."`), since it's a developer console tool, not
  operator-facing product surface (same boundary as log lines staying English).

**A third round, found on an independent sweep of the whole three layers (`return "`,
`return $"`, `throw ...Exception("` and a broader prose regex) after the above shipped —
same bug class again**:
- **`IPlaybackHost`/`IRecorderHost` play/preview errors** — `PlayEntry`/`PreviewEntry`/
  `PreviewVersion`/`Preview` returned `string?` built from English (`"Start the engine to
  play phrases."` etc.). New `PlaybackErrorCode` enum (`EngineNotLive`/`AudioFileMissing`/
  `MonitorIsCable`) + `PlaybackError(Code, FileName?)` record replace the string everywhere.
  App: new `Services/PlaybackErrorText.cs` maps code → `Strings`, used by `BoardViewModel`
  (Play/TestOnHeadphones/PreviewTake) and `PhraseVersionsViewModel`.
- **`EngineStateChangedEventArgs.Error`** — was `string?`, built once from an `AudioEngine`
  catch block's `ex.Message` for *every* error transition, including two that had nothing to
  do with a caught exception (`DeviceChanged`, `CableStalled` — both were pre-formatted
  English sentences at the throw site). New `EngineErrorReason` enum + `EngineError(Reason,
  Detail?)` record; `StatusViewModel.StateErrorText` (replacing the old `Status.StateError`
  binding) maps `Reason` → `Strings`, falling back to `Detail` only for `DeviceFailure` (see
  scope boundary below).
- **`LibraryArchiveService.StageAudio`** — two `InvalidDataException` throws
  (`"Audio entry too large..."` / `"Total audio too large..."`) escaped the method's own
  `ImportErrorCode` scheme entirely, caught only by the generic exception handler further up
  and reported as `ImportErrorCode.ImportFailed` with the raw English message as
  `ExceptionMessage`. Fixed with a private nested `ImportLimitExceededException(ImportErrorCode
  code)` thrown instead, caught before the generic handler, giving both cases their own codes
  (`AudioEntryTooLarge`/`TotalAudioTooLarge`) instead of falling through to the catch-all.

**A fourth item, found by tracing one line from the third-round sweep instead of dismissing
it as a hardware backstop** (`EngineFormat.cs`'s "Source has N channels" message was first
triaged as "never reachable with the app's own devices" — that triage was wrong and got
caught before shipping):
- Traced what `WasapiCaptureDevice.Format` actually hands `Recorder`/`MicPassthrough`: NAudio's
  raw capture `WaveFormat` (the device's shared-mode mix format), not a format clamped to
  mono/stereo first. A real multi-capsule USB mic (4+ channels) reaches `EngineFormat.Convert`
  on an ordinary `Start()` — an operator path, not a can't-happen guardrail. Its sibling,
  `ChannelAdapter.Match`'s "cannot adapt" throw, traced the other way: the engine mixer's output
  format (`AudioFormats.Engine`) is hardcoded mono, and every combination of a mono source against
  any target channel count is handled by one of `ChannelAdapter`'s non-throwing branches — so that
  one throw really is unreachable in production and correctly stays a plain `NotSupportedException`
  (unchanged).
  - New `UnsupportedChannelCountException(int Channels)` (`Dsp/EngineFormat.cs`) replaces the
    raw `NotSupportedException` at the mic-channel-count throw.
  - Same trace surfaced a sibling leak one call away: `WasapiRenderDevice.Init`'s sample-rate
    mismatch (cable not at 48 kHz) threw a hand-built English `NotSupportedException`, caught by
    the same `AudioEngine.HandleStart` catch-all as the mic case. New
    `UnsupportedSampleRateException` (`AudioFormats.cs`, so both `Audio` and `Audio.Wasapi` can
    use it — `Wasapi` depends on `Audio`, not the other way, so the type had to live on the
    `Audio` side) replaces it.
  - `AudioEngine.HandleStart` gained two specific `catch` clauses ahead of the existing generic
    one, mapping to new `EngineErrorReason.TooManyMicChannels` (carries `EngineError.Channels`)
    and `.CableSampleRateMismatch`. The watchdog rebuild path's generic catch is untouched on
    purpose — it never displays anything (transient-retry only), so there was nothing to leak
    there regardless of exception type.

**Scope boundary for Stage 3 (what stays English, and why)** — worth writing down explicitly
so a future sweep doesn't reopen settled ground:
- **Localized**: every code above, plus everything from the original 4-item Stage 3 scope.
  Rule: an operator-reachable path carrying data our own code produced gets a code + `Strings`
  entry, however unlikely the trigger (a 4-channel mic, a cable not at 48 kHz) — "rare" is not
  "can't happen."
- **Stays English, by name**: the dev CLI (`Program.cs`), all `_log(...)` lines everywhere (logs
  are explicitly out of scope per the original plan), `EngineErrorReason.DeviceFailure`'s
  `Detail` (a caught exception's `ex.Message` — unpredictable OS/COM/driver text we cannot
  enumerate, same boundary as `ImportResult.ExceptionMessage`), and `ChannelAdapter.Match`'s
  "cannot adapt" throw (confirmed unreachable with the app's own mono-only mixer — a true
  can't-happen backstop, not an operator path). One-line rule: *operator path with data our
  code produced → localize it; crafted-input guardrail or a genuinely can't-happen backstop →
  leave it English.*

Verified: `dotnet build AdaVoice.slnx` clean; Audio 101 / Core 107 / Host 12 / Wasapi 8 / App
292 (both modes) all green — no regressions against the pre-Stage-3 baseline, and the new
counts are exactly the tests added for the fourth-round fix (3 in Audio.Tests, 4 in
App.Tests). Two wizard screenshots (environment checks, calibration) and the Manage
Categories screenshot visually spot-checked — the new converter/binding paths render real
text, not blank, and the disabled default row looks correct. Every test touched by rounds
2–4 asserts on the code/enum (`ErrorCode`, `PlaybackErrorCode`, `EngineErrorReason`,
`ex.Channels`), never an English substring — same litmus as the environment-check tests.

### Stage 4 — Translate to Ukrainian and Polish

Fill `Strings.uk.resx` and `Strings.pl.resx` for **every** key added in Stage 2 (and the ~30
new ones Stage 3 added splitting Audio/Core/Host's English text into code + `Strings` key —
see the four rounds documented under Stage 3 above) — full coverage including rare technical
errors, per owner's explicit decision when this was scoped. Owner's day-to-day language is **Ukrainian**
— get that one right first; Polish second. Localize the status labels too
(`Status_Live`/`Status_OffAir`/`Status_Degraded`/`Status_Stopped` in `StatusViewModel.cs`) —
she reads those constantly. Until this stage runs, `uk`/`pl` satellites are present but
**empty of these new keys**, so the app currently falls back to English for every key added
this session even if `Language: "uk"` is selected (see `%LOCALAPPDATA%\AdaVoice\settings.json`
— already set to `"uk"` on this dev machine from earlier testing).

### Stage 5 — Fix tests for the refactor

- Pin `CultureInfo.CurrentUICulture` to `en` in test setup (already partly done —
  `TestCultureInitializer.cs`'s module initializer covers plain xunit worker threads;
  `WpfAppFixture.Pump()` covers the WPF STA thread). Re-verify after Stage 3's `Kind`-based
  test updates land.
- Update `AdaVoice.Audio.Tests/Setup/EnvironmentChecksTests.cs` and
  `AdaVoice.App.Tests/FailedCableCheckToVisibilityConverterTests.cs` for the new
  `EnvironmentCheckKind` (Stage 3 dependency).
- Run the full solution suite (`dotnet test AdaVoice.slnx`, expect the 30 PostgreSQL
  integration failures in `AdaVoice.Server.Tests` — pre-existing, no local Postgres in this
  sandbox, unrelated) plus each desktop test project individually.

### Stage 6 — Screenshot-verify theme × language

Extend `tests/AdaVoice.App.Tests/Screenshots/ScreenshotHarness.cs` / `WpfAppFixture.cs` from
*theme*-only (`ADAVOICE_SCREENSHOT_THEME`) to *theme × language* — set `Strings.Culture`-
equivalent (i.e. `CultureInfo.CurrentUICulture`, since the harness controls its own thread)
per render and re-run the full `WindowScreenshotTests.cs` set in `uk` and `pl`. **Ukrainian
gets the primary scrutiny** (owner's decision — that's the language actually used). Check
every window for clipped/overflowing text: Ukrainian and Polish run 30–50% longer than
English, and several places are fixed-size — the phrase tile (148×128,
`MainWindow.xaml`), dialog buttons, `ComboBox` widths in `SettingsWindow.xaml`. This is
where real defects will surface, not in the plumbing (already proven correct by
`StringsTests.cs`).

### Stage 7 — Translated install guides + beta.2 re-release

- Write `INSTALL.uk.md` and `INSTALL.pl.md` (translations of the existing `INSTALL.md` from
  the beta-release session), link all three from the top of `INSTALL.md`.
- Verify satellite assemblies ship: after `scripts/publish.ps1`, confirm `uk/` and `pl/`
  folders (each with `AdaVoice.App.resources.dll`) exist in the publish output **and inside
  the zip** — nothing sets `SatelliteResourceLanguages` to prune them (worth grepping
  `AdaVoice.App.csproj` to be sure Stage 3+ work didn't add one).
- Live-verify the language switch: run the published build, pick Ukrainian in Settings,
  accept the restart, confirm the app comes back fully in Ukrainian (menus, toasts, wizard,
  status labels). Repeat for Polish. **Back up `%LOCALAPPDATA%\AdaVoice` before and restore
  after** — established pattern this session used repeatedly (real settings.json/library.json
  get touched by a live run).
- Tag `v0.1.0-beta.2`, `gh release create` with the new zip + notes mentioning the three
  languages, linking `INSTALL.uk.md` prominently.

## Quick verification commands for picking this back up

```bash
dotnet build AdaVoice.slnx
dotnet test tests/AdaVoice.Core.Tests
dotnet test tests/AdaVoice.Audio.Tests
dotnet test tests/AdaVoice.Audio.Wasapi.Tests
dotnet test tests/AdaVoice.Host.Tests
ADAVOICE_SCREENSHOTS=1 dotnet test tests/AdaVoice.App.Tests   # expect 288/288
```
