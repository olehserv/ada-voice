# UX structural fix plan — 2026-07-12

Sized to what [the audit](../audits/ux-layout-style-audit.md) actually found, not the full
5-pass template — several passes have little or nothing to do, and that's noted rather than
padded. Each pass becomes its own Phase 5 implementation turn (one window at a time, stop for
approval after each), per the workflow's critical rules.

## Status (read this first when resuming)

| Pass | What | Status |
|---|---|---|
| 1 | Modal/dialog resizing stabilization | ✅ No work needed (verified in audit) |
| 2 | `SettingsWindow` footer fix (B1) | ✅ **Shipped** — commits `4c41715`, `26b4dd0`, `fa85f18` (2 fix rounds — a stray full-file `xstyler` reformat, then a margin regression). Review: [settings-window-review.md](../screenshots/review/settings-window-review.md) |
| 4 | Button/action consistency (D1, D2) | ✅ **Shipped** — commit `c47167c`. Review: [button-consistency-review.md](../screenshots/review/button-consistency-review.md) |
| 3 | `MainWindow` resize verification (C1) | ✅ **Verified, no fix needed** — see Pass 3 below |
| 6 | Phrase tile fixed size + tag overflow (F1, expanded) | ⬜ **Not started** — owner request 2026-07-12, promoted ahead of Pass 2b |
| 2b | `MessageBox` → `ContentDialog` (E2) | ⬜ **Not started** — biggest/riskiest remaining item |
| 5 | Form layout cleanup | ✅ No work needed (verified in audit) |

**A separate, larger batch of ad-hoc owner UX feedback** (not audit findings — direct feedback
after reviewing Pass 2/4 screenshots) was also implemented on `SettingsWindow`,
`ManageCategoriesDialog`, and `ManageConversationsDialog`: Done buttons → green, row-height
consistency, icon-only delete/remove buttons, swatch-only color dropdown, removing per-row
Save buttons in favor of auto-persist-on-blur, new-conversation Add on one line + green
checkmark. See commits `29a514d`..`a3c75af` and
[manage-categories-rework-review.md](../screenshots/review/manage-categories-rework-review.md) /
[manage-conversations-rework-review.md](../screenshots/review/manage-conversations-rework-review.md).
The resulting conventions (Done=Success on autosave dialogs, Danger on icon-only destructive
buttons, matched row heights, auto-persist pattern) are now documented in
[wpf-ux-design-rules.md](../wpf-ux-design-rules.md) rule 5 — follow them for any further work
on these dialogs or new ones like them.

**Next step: Pass 6, then Pass 2b — both still need approval to start.** Pass 6 was added
2026-07-12 from direct owner feedback (not a re-run of the audit) and moved ahead of Pass 2b in
the suggested order — see below.

## Pass 1 — Modal/dialog resizing stabilization

**Status: no work needed.** All 8 dialogs already use `ResizeMode="NoResize"` with either
`SizeToContent="Height"` + `MaxHeight`, or a fixed size for dialogs with swapping content
states (`RecorderDialog`). Verified by direct read of every dialog's root element during the
audit. Nothing to implement; this pass exists in the plan only to record that it was checked.

## Pass 2 — Fixed footer action bars (audit B1) — ✅ SHIPPED

**Target:** `src/AdaVoice.App/SettingsWindow.xaml`

**Exact change:** restructure the root `Grid` from 2 rows (TitleBar, content) to 3
(TitleBar, `ScrollViewer` content, footer), moving the "Done" button (currently line 86, inside
the scrollable `StackPanel`) into a new `Grid.Row="2"` footer outside the `ScrollViewer`. No
other content changes — same panels, same bindings, same `DataContext`.

```xml
<Grid.RowDefinitions>
  <RowDefinition Height="Auto" />  <!-- TitleBar -->
  <RowDefinition Height="*" />     <!-- ScrollViewer: Levels/Behavior/Language&Backup only -->
  <RowDefinition Height="Auto" />  <!-- Footer: Done -->
</Grid.RowDefinitions>
```

**Risk:** Low. Pure layout move — no bindings, commands, or code-behind touched. `IsCancel`
stays on the button so `Esc` still closes the window.

**Verification:**
1. `dotnet build AdaVoice.slnx`
2. `xstyler` on the file
3. `dotnet test tests/AdaVoice.App.Tests` (full project — `SettingsWindow` has view-model
   tests that must stay green)
4. Screenshot: default content (matches existing `settings.png`) **and** a manually-extended
   fixture or manual run with enough content to force scroll, to actually prove the footer
   stays put (the existing fixture is too short — see the screenshot inventory's noted gap)

**Rollback:** revert the single file; no other file references the moved button.

**Actual outcome:** shipped, but took 2 fix rounds beyond the plan above — see
[settings-window-review.md](../screenshots/review/settings-window-review.md) for what
actually happened (a stray whole-file `xstyler` reformat, then a margin regression the
automated reviewer missed but a screenshot caught). Also received the separate green-Done /
row-height owner feedback afterward (see the Status table above).

## Pass 2b — Replace `MessageBox` with `ui:ContentDialog` (audit E2)

**Target:** `src/AdaVoice.App/MainWindow.xaml.cs` (6 call sites: `ConfirmDelete`,
`PickImportFile`, `ConfirmAndRestart`, `ShowError`, `ShowInfo`). **Not** `App.xaml.cs` (the
2 system-level calls there stay `MessageBox` — see audit's open question 1).

**Exact change:** introduce `IContentDialogService` (WPF-UI 4.3, already referenced), wire it
via constructor injection alongside the existing dependencies, and replace each `MessageBox.Show`
body with `ShowSimpleDialogAsync`/`ContentDialogResult`, following CTRL-008 in the
dotnet-wpf-design skill. Each method that becomes `async` must return `Task`, not stay
`void`/synchronous — this changes call-site signatures (`ConfirmDelete`, `PickImportFile`, and
`ConfirmAndRestart` are currently synchronous `Func<T,bool>`/similar callbacks the ViewModels
call directly).

**Risk:** Medium — this is the one pass that touches method signatures consumed by ViewModels
(the `Func<PhraseItemViewModel, bool>` style callbacks). Making them `async Task<bool>` ripples
into `BoardViewModel`/`SettingsWindowViewModel` call sites, which must `await` instead of
reading a return value synchronously. This is more than a layout change — flag for extra review
and its own test pass before merging.

**Verification:** same 5 steps as Pass 2, plus explicit manual exercise of each of the 5 flows
(delete a phrase, import merge/replace, language-change restart prompt, an export failure, an
import success) since dialogs are also a common tests-can't-see gap in this codebase (already a
theme in `handoff.md`'s "needs a hardware/user smoke test" notes).

**Recommendation:** do this pass *last* among the structural fixes, and only after Pass 2/3/4
are approved — it's the highest-effort, highest-risk item, and the other three are quick,
low-risk wins that shouldn't wait on it.

**Rollback:** revert `MainWindow.xaml.cs`; no XAML changes required for this pass (the dialogs
are raised entirely from code-behind today).

## Pass 3 — MainWindow layout stability (audit C1) — ✅ VERIFIED, NO FIX NEEDED

**Status: investigation, not a code change.** `MinWidth`/`MinHeight` are already set and
correct. There was no `MaxWidth`/`MaxHeight`, and the single shipped layout had never been
verified above the 420×640 default screenshot size.

**Action taken:** rather than a manual live-app resize, added a permanent regression screenshot
— `WindowScreenshotTests.MainWindow_board_wide` renders `MainWindow` at 1366×780 (a typical
laptop width, chosen to fit this dev machine's 1536×816 work area) with 10 phrases (up from the
default 4) so the `WrapPanel` actually has to reflow across multiple rows. Saved as
`docs/ui/screenshots/after/main-board-wide.png`.

**Result:** confirmed at 1366×780 — the search box stretches full-width with no wrap or clip,
the two filter menu buttons + Record stay on one line (the `*`-width Grid column between them
absorbs the extra space), the phrase `WrapPanel` reflows cleanly into as many columns as fit
with fixed tile sizes, and STOP stays full-width and readable with nothing overlapping. Finding
closed: **verified, no fix needed.** (One cosmetic observation, not a C1 defect: on a short
board with a tall window, there's a large empty gap between the last phrase row and STOP —
expected consequence of the `ScrollViewer`'s `Grid.Row="*"` sizing, not a wrap/clip/overlap bug;
not actioned here.)

**Explicitly out of scope for this pass:** building the Full/Docked 720 px responsive layout —
that's a separate, already-owned feature decision
([ui-ux-localization-scope.md](../ui-ux-localization-scope.md) slice 3), not a structural bug
fix. Don't fold it into this UX pass without a separate go-ahead.

**Risk:** none — no product code changed, only a new test.

## Pass 4 — Button/action consistency (audit D1, D2) — ✅ SHIPPED

**Targets:**
- `ManageCategoriesDialog.xaml` (Delete button, line ~53)
- `ManageConversationsDialog.xaml` (Delete button ~63, Remove button ~89)
- `PhraseVersionsDialog.xaml` (✕ delete button ~70)
- `RecorderDialog.xaml` (Save button, line 63 — add `IsDefault="True"`)

**Exact change:** `Appearance="Secondary"` → `Appearance="Danger"` on the 4 destructive
buttons above; add `IsDefault="True"` to Recorder's Save button (safe because Record/Stop/Save
are mutually exclusive via `Visibility`, so only one `IsDefault` button is ever active at once).

**Risk:** Low. Single-attribute changes, no logic touched.

**Verification:** build, `xstyler`, full test run (no behavior changes expected, but the
screenshot set should visibly show the color change), screenshot each of the 4 files.

**Rollback:** revert the attribute per file — fully independent, can be done/undone per file.

**Actual outcome:** shipped exactly as planned, clean on first review — see
[button-consistency-review.md](../screenshots/review/button-consistency-review.md).

## Pass 6 — Phrase tile fixed size + tag overflow indicator (audit F1, expanded)

**Status: not started.** Added 2026-07-12 from direct owner feedback while reviewing the board
screenshots (not a re-run of the audit) and promoted ahead of Pass 2b. This absorbs and expands
the audit's original **F1** finding (tile height varies with title-wrap length), which had been
deferred as low/cosmetic — the owner now wants it fixed directly, plus a second, related problem
the audit didn't cover: tile height/width also varies with **tag count**, and there's no cap on
how many tags a tile can show.

**Problem:** in `MainWindow.xaml`'s phrase tile `DataTemplate` (~lines 224–332), the tile's
outer container has no explicit size — height and width both grow with content. A phrase with
0 tags renders visibly shorter than one with 2 tags; a phrase with a long title (original F1)
renders taller still. In the board's `WrapPanel`, mismatched tile heights within the same row
look uneven. There's also no upper bound on tag count — a phrase with many tags could grow a
tile arbitrarily tall.

**Target:** `src/AdaVoice.App/MainWindow.xaml` (phrase tile `DataTemplate`), and, if the
recommended fixed-count cap (Option A below) is chosen, `src/AdaVoice.App/ViewModels/
PhraseItemViewModel.cs` + `BoardViewModel.cs:556` (where `TagChips` is populated per tile today).

**Proposed change:**
1. **Fixed tile size** — give the tile an explicit `Width`/`Height` so every tile in the
   `WrapPanel` is identical regardless of tag count or title length. Reserve room for: category
   edge marker + title (existing 2-line wrap, `MaxWidth="130"`) + duration + at most one row of
   tag chips. Exact pixel values to be tuned during implementation and checked against the
   `MainWindow_board_wide` screenshot test (added for Pass 3) — its 10 sample phrases already mix
   0/1/2-tag phrases, so it doubles as a ready-made regression check for this pass.
2. **Title overflow** — with a fixed tile height, a title that would wrap to a 3rd line needs a
   hard stop: add `TextTrimming="CharacterEllipsis"` (with a matching `MaxHeight`) on top of the
   existing `TextWrapping="Wrap"`, so a very long title clips with an ellipsis instead of
   pushing content out of the fixed box. This folds the original F1 problem into the same fix.
3. **Tag overflow indicator** — cap visible tags to a small fixed count (start at 2, tune by
   eye) and show a trailing "+N" chip when there are more, styled like the existing tag chip
   (`Scrim.Tag` background) but with a neutral/secondary border since it isn't a real tag. No
   click behavior needed on the chip itself — the tile's existing context menu already has
   "Edit…", which opens `PhraseEditDialog` and lists every tag in full (verified:
   `PhraseEditDialog.xaml` line 43's `ItemsControl` over the complete `Tags` collection). That
   already satisfies "the user can see all tags by opening the phrase's details" — no new dialog
   needed.

**Decision needed before implementation (flag for approval, not resolved here):** where the
"cap to N, show +N more" logic lives.
- **Option A — fixed-count cap (recommended).** `BoardViewModel` (which already populates
  `TagChips` per item) also computes a bounded list + overflow count, exposed as new
  `PhraseItemViewModel` properties (e.g. `VisibleTagChips`, `HiddenTagCount`). Deterministic, no
  runtime measurement, a small and contained ViewModel change — matches the project's existing
  pattern of view-model-computed display state (same shape as `DurationLabel`).
- **Option B — true width-measured overflow** (show only as many chips as actually fit the
  tile's pixel width). More visually precise — two short tags might both fit where two long tags
  wouldn't — but needs a measuring behavior/converter in code-behind, real extra machinery for a
  cosmetic feature. Not recommended given the project's "good enough for now" bias unless Option
  A looks visually wrong once tried.

**Risk:** Low-medium. Mostly a visual change, plus (if Option A) one small, contained ViewModel
addition — no behavior change to editing, deleting, or playing phrases.

**Verification:** same 5 steps as other passes — build, `xstyler`, full `AdaVoice.App.Tests` run
(a new bounded tag-list property needs its own small unit tests alongside the existing
`TagChips` coverage), screenshot the board with a mix of 0/1/2/5+-tag phrases (extend the
`MainWindow_board_wide` fixture added for Pass 3 rather than building a new one), visual review.

**Rollback:** revert the XAML file (and the ViewModel addition, if made) — independent of
Pass 2b/3.

## Pass 5 — Form layout cleanup

**Status: no work needed.** The audit found labels aligned, sections grouped in
`PanelStyle` borders with consistent margins, and no validation-placement problems in any
current form. Nothing to implement. Revisit only if a future form (e.g. a Devices group,
per `handoff.md`'s open follow-ups) is added without following the rules in
[wpf-ux-design-rules.md §4](../wpf-ux-design-rules.md#4-form-ux-rules).

## Suggested implementation order

1. ✅ Pass 2 (Settings footer) — done.
2. ✅ Pass 4 (button semantics) — done.
3. ✅ Pass 3 (MainWindow verification) — done, verified no fix needed.
4. **Pass 6 (phrase tile fixed size + tag overflow)** — owner priority, added 2026-07-12;
   do this before Pass 2b since it's lower-risk (mostly visual, at most one small ViewModel
   addition) than the dialog-signature rewrite below.
5. Pass 2b (ContentDialog migration) — last, biggest, needs its own careful review.
6. Pass 1 and 5 — already done; no implementation turn needed, just recorded here.

**Waiting for approval before starting Pass 6.**
