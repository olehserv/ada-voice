# Board Library UI — round 2 (smoke-test feedback)

> **Status: all 3 slices implemented and committed on `feat/board-library-ui`.** Slice 1 smoked and
> confirmed working by the user. Slice 2 smoked, confirmed, then refined again after a follow-up
> round of feedback (dropdown colour picker + 20 colours, tile-corner fix, seconds not ms) — that
> round is also committed. Slice 3 (tag registry + chip editor + tag chips on tiles) is implemented
> and unit-verified but not yet smoked — see `handoff.md` for the smoke checklist. Once Slice 3 is
> smoked, the branch is merge-ready.
>
> Round 1 (edit/delete/search/categories) is on `feat/board-library-ui`. This round addresses the
> 7 items from the interactive smoke run. Work continues on the same branch.

## Context

A real interactive smoke run of round 1 produced 7 pieces of feedback — a mix of genuine bugs and
UX improvements. This plan addresses all 7, grouped into three shippable slices. Decisions taken
with the user:

- **Category colour on a phrase button: full colour fill** (text auto-switches black/white for
  contrast). This **overrides** `docs/design/09-design-system.md` ("phrase buttons stay
  neutral"). The user owns the product; the design doc is updated to match (Task 2B).
- **Clicking a phrase stays call-only** (plays to the call when Live; otherwise a short notice).
  Testing on headphones is an explicit **right-click "Test on headphones"** action.
- **Tag colours come from a curated palette**; tags render as **rounded chips: colour border,
  colour text, fill-independent dark background**.
- **Localization stays deferred** — new strings are English-only (tracked debt).

The 7 items → tasks: #1 → 1A, #7 → 1A, #4 → 1B, #3 → 1C, #2 → 2A+2B, #5 → 3A+3B, #6 → 3C.

---

## Slice 1 — Bug fixes & quick wins (no domain change)

### Task 1A — Phrase buttons editable when stopped (#1) + test-to-headphones (#7)
**Root cause of #1:** the phrase button is disabled when not Live, and WPF will not open a
context menu on a disabled control, so Edit/Delete vanish. Broken phrases have the same problem.

- `MainWindow.xaml`: **remove the `IsEnabled` disabling** from the phrase button (both the
  `Status.IsLive` setter and the broken `IsEnabled=False` trigger). Keep the broken **dim**
  (opacity) and badge — visual only, so the right-click menu still opens.
- `BoardViewModel.Play` (`PlayCommand`): gate the action instead of the control — if broken →
  `Notice = "audio missing"`; if not Live → `Notice = "Start the engine to play to the call."`;
  else `_playback.PlayEntry`.
- Add **"Test on headphones"** to the phrase context menu (above Edit/Delete).
  - Seam: add `string? PreviewEntry(PhraseEntry entry)` to `IPlaybackHost`
    (`src/AdaVoice.Host/IPlaybackHost.cs`); `EngineHost` already implements `PreviewEntry`
    (plays to the monitor, refuses the cable, engine-independent) — just expose it on the seam.
    Update `FakePlaybackHost`.
  - `BoardViewModel.TestOnHeadphones(PhraseItemViewModel)` command: run `PreviewEntry` on a
    **background thread** (`Task.Run`) so the UI does not freeze (Preview blocks until playback
    ends); marshal any returned error string back to `Notice` via `_onUiThread`.
- Tests (`BoardViewModelTests`): Play when stopped sets a notice and does not call the host;
  Play when Live calls `PlayEntry`; `TestOnHeadphones` calls `PreviewEntry`.

### Task 1B — Engine control buttons reflect state (#4)
- `StatusViewModel`: add computed bools, all refreshed by the existing `State`
  `[NotifyPropertyChangedFor]` (add them to that attribute list):
  - `CanStart => State == Stopped`
  - `CanStopEngine => State != Stopped`
  - `CanToggleOffAir => State is Live or OffAir`
  - `CanStopPhrase => State == Live`
- `MainWindow.xaml`: bind each engine button's `IsEnabled` to the matching flag
  (`Start`, `Stop engine`, `OFF AIR`, big `STOP`).
- Tests (`StatusViewModelTests`): the four flags for Stopped / Live / OffAir / Degraded.

### Task 1C — Remember window size & position (#3)
- `Settings` (`src/AdaVoice.Core/Domain/Settings.cs`): add `double? WindowWidth/WindowHeight/
  WindowLeft/WindowTop` (nullable = "never saved, use defaults").
- `ISettingsHost` + `EngineHost`: add `WindowPlacement? WindowPlacement { get; }` (a small record
  of the four values) and `void SaveWindowPlacement(double w, double h, double left, double top)`
  → updates in-memory `Settings` and saves via `_settingsRepository`.
- `SettingsViewModel`: expose `WindowPlacement` (passthrough) + `SaveWindowPlacement`.
- `MainWindow`: in `OnSourceInitialized` (before first render → no flash), if a placement is
  saved, apply it **clamped to `SystemParameters.VirtualScreen`** (guard against an off-screen
  monitor that was unplugged). In `OnClosing`, save current `Width/Height/Left/Top`.
- Test (`JsonSettingsRepositoryTests`): round-trip the new fields.

---

## Slice 2 — Category colour fill (#2)

### Task 2A — Colour swatch picker in the category manager (replace the hex box)
- Add a curated palette constant (≈8 hex colours that read well on `#1F1F1F`, e.g. the existing
  Accent `#4CC2FF`, `#54D262`, `#F2A33C`, `#FF6B6B`, `#B98AFF`, `#4FD1C5`, `#F06595`, `#FFD43B`).
- `ManageCategoriesDialog.xaml`: replace each row's colour `TextBox` (and the add-row's) with a
  small swatch `ItemsControl` (palette colours as clickable squares; the selected one ringed).
  Bind to `CategoryRowViewModel.Color` / `CategoriesViewModel.NewColor` (still stored as hex).

### Task 2B — Fill the phrase button with its category colour (auto-contrast EVERYTHING)
A saturated fill collides with **every** mark on the button, not just the title. Derive **one**
contrast brush from the category colour and drive all foreground marks with it.

- **Read `Theme/Controls.xaml` `PhraseButtonStyle` first.** If the playing-glow (`IsPlaying`) is a
  **background** tint, the category fill will hide it — switch the playing indicator to a
  **border/ring** (Accent or white) so it reads over any fill. Decide this from the actual style
  before touching the template.
- `PhraseItemViewModel`: add `CategoryColor` (hex), resolved from a `categoryId → colour` map
  `BoardViewModel` builds from `_library.Categories`; refresh on edit (category change) and after
  the category manager closes (recolour).
- Converters (new `src/AdaVoice.App/Converters.cs`, registered in `App.xaml`):
  - `HexToBrushConverter` (hex → `SolidColorBrush`).
  - `ContrastTextConverter` (hex → black **or** white brush via a **WCAG-style** contrast pick,
    not a crude 0.5 luminance cut — get it right once).
- `MainWindow.xaml` phrase template:
  - `Background` ← `CategoryColor` via `HexToBrush`.
  - **Title, duration, AND the "audio missing" badge** ← the **same** `ContrastText` brush
    (do not leave duration on fixed `Text.Secondary` grey or the badge on `Status.OffAir` orange —
    both go illegible on many fills).
  - Playing indicator = border/ring (per the Controls.xaml decision above); broken-dim = opacity.
- Update `docs/design/09-design-system.md` §"Category colors" to record the override (filled
  buttons + colored tag chips, not neutral) so the doc and the app agree.

---

## Slice 3 — Colored, reusable tags (#5 + #6)

### Task 3A — Tag registry in the domain (TDD)
- New `src/AdaVoice.Core/Domain/TagInfo.cs`: `record TagInfo { string Name; string Color; }`.
- `Library`: add `List<TagInfo> Tags { get; init; } = []` (a name→colour registry; serializes via
  the existing camelCase `LibraryJson` options — no custom converter needed).
- `PhraseLibraryService`:
  - When `SetPhraseTags` normalizes names, **register any new name** with the next palette colour
    (cycle the curated palette by `Tags.Count`); persist. Phrase storage stays `string[]` of names.
  - Expose `IReadOnlyList<TagInfo> Tags` (for suggestions + chip colours).
- Tests (`CategoryAndTagTests` or new `TagRegistryTests`): a new tag gets a palette colour and
  persists; an existing tag keeps its colour; colours cycle; reload round-trips the registry.

### Task 3B — Seam + chip editor in the edit dialog
- `ILibraryHost` + `EngineHost` + `FakePlaybackHost`: expose `IReadOnlyList<TagInfo> Tags`.
- `PhraseEditViewModel`: replace the comma `TagsText` model with a **chip editor**:
  - `ObservableCollection<string> Tags` (current tags) with `AddTag(name)` / `RemoveTag(name)`.
  - `Suggestions` = registry tag names not already on the phrase (for "add an existing tag").
  - `Save` writes the chip list via `SetPhraseTags` (registration happens in the service).
  - Keep it pure/testable; tests cover add/remove/dedup and suggestions. **Migrate the existing
    `PhraseEditViewModelTests` "Save_applies…parsed_tags" test** off `TagsText` to the chip model
    (it will otherwise fail to compile).
- `PhraseEditDialog.xaml`: current tags as removable chips (chip + "×"); a text box + Add for a
  new tag; clickable suggestion chips for existing tags.

### Task 3C — Tag chips on the phrase button (#6)
- `PhraseItemViewModel`: expose the phrase's tags as `TagChipViewModel { Name; Color }` resolved
  from the registry (rebuild on edit). `BoardViewModel` passes a name→colour lookup.
- `MainWindow.xaml` phrase template: an `ItemsControl`/`WrapPanel` of chips under the title —
  **rounded rectangle, colour border + colour text** (per the user's spec). Reuse
  `HexToBrushConverter` for border/text. **Chip background must be fill-independent** — a plain
  grey (`Surface.Raised` #2B2B2B) barely separates on dark category fills, so use a fixed
  semi-transparent dark scrim (≈`#000` @ 40%) that reads on any category colour.

---

## Verification

- **Unit (CI):** `dotnet test --nologo` — all suites green, incl. new tests for the gated Play,
  the `Can*` engine flags, the settings window-placement round-trip, the tag registry, and the
  chip-editor VM.
- **Build:** `dotnet build src/AdaVoice.App --nologo` clean; app smoke-launches.
- **Manual smoke (running app):**
  - **#1/#7:** engine Stopped → right-click a phrase → **Edit…**/**Delete**/**Test on
    headphones** all work; Test plays to headphones without freezing the window; left-click while
    Stopped shows the "Start the engine" notice.
  - **#4:** Start disabled once running; Stop engine/OFF AIR/STOP disabled while Stopped; states
    flip correctly Live↔OffAir.
  - **#3:** resize + move the window, close, reopen → it returns to the same size/position.
  - **#2:** phrase buttons are filled with their category colour, text stays readable; the
    category manager picks colour from swatches (no hex typing).
  - **#5/#6:** edit a phrase → add/remove tag chips, add an existing tag from suggestions; the
    phrase button shows the tags as bordered colour chips; colours persist across restart.

## Notes / lessons
- **#1 lesson — disabled controls swallow their context menu.** Gate the *action* (command),
  not the *control*, when the control still needs right-click affordances. Disable a control only
  when there is genuinely nothing to do with it.
- **Design doc vs product owner.** The full-fill choice overrides design-09; the right move is to
  update the doc, not silently diverge — otherwise the doc stops being trustworthy.
- **Tag model kept light:** a name→colour registry on the library, not a full Tag entity with
  ids/foreign keys. Right-sized for "a few dozen phrases"; phrases still store tag names.
