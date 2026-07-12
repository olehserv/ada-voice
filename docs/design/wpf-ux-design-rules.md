# AdaVoice — WPF UX Design Rules

Project-specific rulebook for **layout mechanics and interaction patterns**. This is not where
tokens, colors, or typography live — that's [09-design-system.md](09-design-system.md)
(canonical) and [05-ui-design.md](05-ui-design.md) (per-screen specs). This document exists so
"is this dialog shaped right?" and "where does this button go?" have one answer, project-wide.

Findings that motivated these rules are in
[audits/ux-layout-style-audit.md](audits/ux-layout-style-audit.md).

## 1. Product UX principles

From [10-ui-redesign-brief.md](10-ui-redesign-brief.md) and 09 — restated here because they
drive the *mechanical* rules below, not just visuals:

- **Invisible when it's working.** The operator should stop noticing the app exists mid-call.
- **Predictable over clever.** Same button in the same place, every screen, every time.
- **Fast under pressure.** Primary actions reachable without scrolling, searching, or resizing.
- **Calm, not loud.** One accent color, no decorative motion, status conveyed by dot + text
  + color together (never color alone).

## 2. Window behavior rules

- **Every dialog is `ResizeMode="NoResize"`.** AdaVoice dialogs are fixed-purpose forms, not
  documents — resizing them buys nothing and risks the exact "layout breaks on resize" bug
  this rulebook exists to prevent. This is already the rule in practice (audit confirmed all
  8 dialogs); keep it for every new dialog.
- **`MainWindow` is the only resizable window.** It must keep an explicit `MinWidth`/
  `MinHeight` (currently 420×560 — do not lower without re-verifying the layout at that size).
- Every dialog: `WindowStartupLocation="CenterOwner"`, `ShowInTaskbar="False"`,
  `Owner` set by the caller.
- Prefer `SizeToContent="Height"` with an explicit `Width` and a `MaxHeight` cap for dialogs
  whose content can grow (lists, forms). Use a fixed `Width`+`Height` only when the content
  swaps between differently-sized states at runtime (see `RecorderDialog` — `SizeToContent`
  would make the window visibly jump between idle/recording/save-form).

## 3. Dialog layout rules — fixed header/content/footer

**Required structure for any dialog with a `ScrollViewer`:**

```xml
<Grid>
  <Grid.RowDefinitions>
    <RowDefinition Height="Auto" />  <!-- ui:TitleBar -->
    <RowDefinition Height="*" />     <!-- ScrollViewer: content only -->
    <RowDefinition Height="Auto" />  <!-- Footer: primary/secondary actions, NEVER inside the ScrollViewer -->
  </Grid.RowDefinitions>
</Grid>
```

- The primary dismiss/commit button (`Done`, `Close`, `Save`, `Cancel`) always lives in the
  **footer row**, outside the `ScrollViewer`. This was audit finding B1 — `SettingsWindow` was
  the one place it violated this; **fixed in Pass 2** (see
  [screenshots/review/settings-window-review.md](screenshots/review/settings-window-review.md)).
  Every dialog now does this correctly; `ManageCategoriesDialog`, `ManageConversationsDialog`,
  `PhraseVersionsDialog` remain the reference examples.
- Per-row action buttons *inside* a list (e.g. a row's own "Delete" in `ManageCategoriesDialog`)
  are fine inside the `ScrollViewer` — the rule is about the dialog's own primary action, not
  every button on the screen. (Per-row "Save" buttons used to be an example here too, but
  `ManageCategoriesDialog`/`ManageConversationsDialog` no longer have them — see rule 5's
  auto-persist note.)
- Dialogs short enough to never need a `ScrollViewer` (`PhraseEditDialog`, `RepairPhraseDialog`,
  `RecorderDialog`) don't need this structure — don't add a `ScrollViewer` "for consistency"
  where the content already fits.

## 4. Form UX rules

- Labels: `VerticalAlignment="Center"`, `MinWidth` set so labels in the same form align
  (see `SettingsWindow`'s `MinWidth="70"` label pattern).
- Section grouping: wrap related fields in `Border Style="{StaticResource PanelStyle}"` with
  `Margin="0,0,0,12"` between panels (existing convention — keep it).
- Validation: inline, next to the field it concerns; never a separate summary area (none of the
  current forms need this yet — revisit if a form grows real validation rules).
- If a form ever grows past one screen of content without a natural section break, split it
  into a second dialog rather than making it scroll — scrolling forms should be the exception,
  not the default (only `SettingsWindow` currently scrolls, and only because of Levels +
  Behavior + Language & Backup being three logically separate groups on one screen).

## 5. Button/action rules

- **Order:** secondary/cancel action to the left, primary action to the right
  (`Cancel` … `Save`). This is the existing convention in `PhraseEditDialog` and
  `RepairPhraseDialog` — apply it to any new dialog with a clear primary action.
- **`IsDefault="True"`** on the primary action, **`IsCancel="True"`** on the dismiss action,
  whenever the dialog has one clear "commit" button (not on pure list-management windows like
  `ManageCategoriesDialog`, where "Done" is the only footer button and there's nothing to
  default to).
- **Destructive actions get `Appearance="Danger"`** (Delete, Remove, Discard-that-loses-work).
  This was audit finding D1 (every destructive button used `Secondary`) — **fixed in Pass 4**:
  category/conversation Delete, conversation-member Remove, and phrase-version delete are all
  `Danger` now. Icon-only destructive buttons (a bare "✕") use `Danger` too — see
  `ManageCategoriesDialog`'s Delete and `ManageConversationsDialog`'s member Remove for the
  reference pattern (owner UX feedback, 2026-07-12).
- **"Done" is `Appearance="Success"` (green) on pure list-management dialogs that auto-persist
  every edit** (`SettingsWindow`, `ManageCategoriesDialog`, `ManageConversationsDialog` — owner
  decision, 2026-07-12): there's no separate "Save" to distinguish it from, so Done doubles as
  the implicit "you're finished" confirmation. **`Close`/`Cancel` stay `Secondary`** on dialogs
  that pair a dismiss action with a distinct primary action elsewhy (`PhraseVersionsDialog`'s
  "Close", `RecorderDialog`'s "Close", `PhraseEditDialog`/`RepairPhraseDialog`/
  `SetupWizardWindow`'s "Cancel") — don't turn those green too; the green signal is specifically
  for "this dialog has no Save step, closing it is the confirm."
- **Auto-persist on blur/selection-change instead of a per-row "Save" button**, for inline-
  editable list rows whose parent dialog has no single primary action (owner decision,
  2026-07-12). Wire the editable `TextBox`'s `LostFocus` and any paired `ComboBox`'s
  `SelectionChanged` to a code-behind handler that calls the existing `[RelayCommand]` — see
  `ManageCategoriesDialog.xaml.cs`/`ManageConversationsDialog.xaml.cs`'s `RowField_Committed`
  (mirrors `SettingsWindow.xaml.cs`'s pre-existing `DuckSlider_Committed` pattern). Do **not**
  add persistence logic to the row ViewModel itself — the view only changes what *triggers* the
  existing Save/Rename command, the ViewModel's public API stays untouched.
- **Minimum interactive target:** 32 px height (WPF-UI default), consistent with the
  [dotnet-wpf-design skill](../../.claude/skills/dotnet-wpf-design/SKILL.md)'s Fluent sizing
  table. When a row mixes a `TextBox`/`ComboBox` with a `Button`, give all of them the SAME
  explicit `Height` (currently `36` — see `ManageCategoriesDialog`/`ManageConversationsDialog`)
  — WPF-UI's default Button height and default ComboBox height don't match on their own, so
  `MinHeight` alone won't fix a row where the button renders shorter than its neighbors.
- **Guard before mutate:** any action that overwrites in-memory state without an obvious undo
  (Discard take, Restore/Reset if one is ever added) confirms *before* touching state — see
  CTRL-008 in the dotnet-wpf-design skill for the exact pattern (confirm as the first line of
  the handler, `Primary` button text names the action, not "OK"/"Yes").

## 6. MainWindow layout rules

- `MinWidth="420" MinHeight="560"` is load-bearing — the shipped single-layout Board is
  designed to hold at exactly 420 px (see 05 §"Window sizing"). Don't shrink it without
  re-checking the search/filter row for wrapping.
- Status panel (Row 0, engine controls + Setup/Settings) and the STOP button (last row) are
  fixed `Auto` rows — they never scroll or shrink. Only the phrase grid (middle `*` row)
  scrolls/wraps.
- The Full/Docked responsive layout (category rail at ≥720 px) is a **known, separately-owned
  open decision** — see
  [ui-ux-localization-scope.md](../../plans/ui-ux-localization-scope.md). Don't build it as a
  side effect of a UX polish pass; it needs its own design decision first.

## 7. WPF layout implementation rules

Project-specific summary — full patterns/anti-patterns live in the
[dotnet-wpf-design skill](../../.claude/skills/dotnet-wpf-design/SKILL.md) (already loaded for
this workstream); read it before touching any window's layout.

- **Grid** for anything with a header/content/footer shape, or aligned label+field rows.
- **StackPanel** only for short inline runs (a toolbar row, a button group) — never as the
  outer container of a dialog with a `ScrollViewer` inside it (StackPanel gives infinite height,
  so the `ScrollViewer` never gets a bounded viewport and won't scroll correctly if nested
  wrong — see the skill's LAYOUT-003).
- **WrapPanel** for the phrase tile grid and the version-tile grid — already correct, keep it
  for any future tile-based collection.
- **DataTemplate.Triggers**, never `Style.Setter.Value`, for any per-row icon/content that
  varies by data (skill's CTRL-003 — the icon-sharing bug). Not hit yet in this codebase; flag
  it as a landmine for the DataGrid-less-but-ItemsControl-heavy views here if one is ever added.

## 8. Visual design rules

Canonical in [09-design-system.md](09-design-system.md). Restated as a hard rule because it's
the thing most likely to regress during a polish pass:

- **No hardcoded hex colors or literal `FontSize` values in view XAML.** Everything comes from
  `Theme/Tokens*.xaml`. Verified clean as of this audit (2026-07-12) — keep it that way.
- **`Appearance="Danger"`** is the only sanctioned way to mark a destructive action — no custom
  red brushes.
- Every new `ui:ContentDialog` (see rule 9) uses `DynamicResource` for anything theme-dependent,
  same as every other view.

## 9. In-flow dialogs: `ContentDialog`, not `MessageBox`

New rule, prompted by audit finding E2: **`System.Windows.MessageBox` is banned in normal app
flow.** It renders OS chrome and is the single biggest visual tell that breaks the Fluent look.

- Use `ui:ContentDialog` via `IContentDialogService` (already available — WPF-UI 4.3 is
  referenced) for confirms, errors, and info prompts raised during normal operation
  (delete-confirm, import merge/replace choice, restart-required confirm, export/import
  error/success).
- **Exception — keep `MessageBox` for the two system-level cases in `App.xaml.cs`:** the
  single-instance notice (shown before any window/theme host exists) and the global unhandled-
  exception handler (shown when the app may already be in a broken state — robustness beats
  polish here). Do not migrate these without re-confirming the ContentDialog host is reliably
  available at that point in the app lifecycle.
- Follow CTRL-008 in the dotnet-wpf-design skill exactly: confirm dialog is the *first* line of
  the handler (before any read/mutation), `Primary` button text names the action
  ("Delete phrase", not "Yes"), dialog lives in code-behind not the ViewModel.

## 10. Voice-assistant / engine state rules

AdaVoice is non-AI today (per [CLAUDE.md](../../../CLAUDE.md)) — there is no
Listening/Processing/Speaking pipeline. The states that actually exist and must stay visually
distinct:

| State | Where shown | Rule |
|---|---|---|
| `Stopped` / `Live` / `OffAir` / `Degraded` (engine) | Status dot + pill (`MainWindow` Row 0) | Dot color + text label together, never color alone (09 already enforces this) |
| Recorder: idle / recording / processing / take-pending | `RecorderDialog` (mutually exclusive via `Visibility`) | Exactly one state visible at a time; window stays a fixed size across all of them (already the rule — see the `SizeToContent` note in `RecorderDialog.xaml`) |
| Playing (a phrase or a version) | Accent ring on the tile border | Constant border thickness so the ring never shifts layout (09 already enforces this) |
| Broken / missing audio | Dimmed tile + inline warning text | Tile stays clickable (Edit/Delete/Test still work) — never fully disable a broken tile |

If a future AI feature adds real Listening/Processing/Speaking states, extend this table before
building the UI for it — don't invent ad-hoc visuals per screen.

## 11. Verification rules

Every change under this workstream must pass, in order:

1. `dotnet build AdaVoice.slnx`
2. `xstyler` (XAML Styler — configured via `dotnet-tools.json`, run as a local tool)
3. `dotnet test tests/AdaVoice.App.Tests --filter "Category=Screenshot"` with
   `ADAVOICE_SCREENSHOTS=1` (and `ADAVOICE_SCREENSHOT_THEME=Light` for the light-theme pass) —
   reuses the existing harness, output goes to `docs/ui/screenshots/after` /
   `after-light`; copy the relevant PNGs into `docs/design/screenshots/after/` for this
   workstream's before/after record.
4. Visual before/after comparison, documented in `docs/design/screenshots/review/`.
5. Full test suite (`dotnet test AdaVoice.slnx`) before considering a screen done, not just the
   targeted project.
6. Stop for approval — one window/screen at a time, per the workflow's critical rules.

## CLAUDE.md pointer

Added a one-line pointer from the root `CLAUDE.md` to this file (see that file's "Design docs"
note) so future sessions find this rulebook without re-deriving it.
