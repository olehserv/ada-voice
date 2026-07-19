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
| 6 | Phrase tile fixed size + tag overflow (F1, expanded) | ✅ **Shipped** — absorbed into commit `41e3a6f` (Phase B, 2026-07-18); one layout bug found + fixed 2026-07-19, see below |
| 2b | `MessageBox` → `ContentDialog` (E2) | ✅ **Shipped** — board `ConfirmDelete` 2026-07-19, remaining 4 SettingsWindow prompts + every other dialog's confirm via Phase C Steps 2–5 (see below) |
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

**Pass 2b landed in two parts, both now shipped (2026-07-19).** Exploration found the plan below
under-specified the modal-`SettingsWindow` host problem (see the updated Pass 2b section) — owner
shipped the low-risk half (`ConfirmDelete`) first, then the remaining 4 prompts + every other
dialog's own confirm via Phase C Steps 2–5 the same day. Pass 6 was added 2026-07-12 from direct
owner feedback, absorbed into the Phase B board rebuild (commit `41e3a6f`, 2026-07-18), and
confirmed correctly shipped 2026-07-19 after fixing a real layout bug found during that
verification (see the Pass 6 section below).

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

## Pass 2b — Replace `MessageBox` with `ui:ContentDialog` (audit E2) — ✅ SHIPPED

**Status: board delete-confirm shipped 2026-07-19; the other 4 prompts (and every other dialog's
own confirm) shipped via Phase C Steps 2–5, also 2026-07-19** — see
`C:\Users\olehs\.claude\plans\check-what-is-planned-temporal-kahn.md` for the full per-step
writeup. The 2 `App.xaml.cs` system-level `MessageBox` calls stay as-is by design (rule 9
exception — single-instance notice, global crash handler).
Exploration ahead of implementation found the plan below (as originally written) under-specified
a real structural problem: **4 of the 5 `MainWindow.xaml.cs` prompts are raised from
`BackupSettingsViewModel`, which lives inside `SettingsWindow` — a *modal* `ui:FluentWindow` shown
via `ShowDialog()` with `Owner = MainWindow`.** A `ContentDialog` renders into one window's visual
tree; hosted in `MainWindow` it would appear *behind* the modal `SettingsWindow` and be
unreachable. Only `ConfirmDelete` is raised from the non-modal board itself, so it was the one
prompt that could migrate cleanly without a second host. Owner chose to ship that now and defer
the rest — see `handoff.md`'s 2026-07-19 entry for the full writeup and the smoke-test evidence.

**What shipped:** `ConfirmDelete` (`MainWindow.xaml.cs`) → `ui:ContentDialogHost` overlay in
`MainWindow.xaml` + a `ContentDialogService` wired in the constructor + `ShowSimpleDialogAsync`,
following CTRL-008 (Primary button says "Delete", not "Yes"; no `Danger` appearance — the delete
keeps a recoverable backup). This forced `BoardViewModel`'s `_confirmDelete` delegate and `Delete`
command to become async (`Func<PhraseItemViewModel, Task<bool>>`, `IAsyncRelayCommand`) — the
ripple the original plan warned about, but contained to one command. `BoardViewModelTests`'s two
Delete tests updated to `await DeleteCommand.ExecuteAsync(...)`.

**Original target (as planned, before the scoping above):** `src/AdaVoice.App/MainWindow.xaml.cs`
(the plan said 6 call sites; exploration found 5: `ConfirmDelete`, `PickImportFile`,
`ConfirmAndRestart`, `ShowError`, `ShowInfo`). `App.xaml.cs`'s 2 system-level calls stay
`MessageBox` regardless (single-instance notice, global crash handler — see audit's open
question 1).

**Deferred to Phase C — `PickImportFile` (merge/replace choice), `ConfirmAndRestart`, `ShowError`,
`ShowInfo`.** All 4 are invoked from `BackupSettingsViewModel`, shown inside the modal
`SettingsWindow`. Migrating them needs a **second** `ContentDialogHost` inside `SettingsWindow.xaml`
and re-sourcing their delegates from `SettingsWindow` instead of `MainWindow` (today they're built
in `BoardViewModel.RunSettings()` from `window.*` method groups, before `SettingsWindow` exists —
see `App.xaml.cs`'s composition order). Also note: `BackupSettingsViewModel.OnLanguageChanged` is a
CommunityToolkit-generated `partial void` hook — it cannot be made `async`, so migrating
`ConfirmAndRestart` needs a fire-and-forget (`_ = ConfirmRestartAsync()`) or a redesign of that
hook, not a direct `await`. Phase C reworks these dialogs anyway (`handoff.md`: "Pass 2b is best
done before/with Phase C") — do the host + re-wiring there.

**Risk (materialized as described):** Medium — this was the one pass that touched method
signatures consumed by ViewModels. Making `_confirmDelete` `async Task<bool>` rippled into
`BoardViewModel.Delete` (now `IAsyncRelayCommand`) and its two unit tests, exactly as flagged.
Scoping to one delegate kept the ripple small.

**Verification (done for the shipped scope):** build + `xstyler` + full `AdaVoice.App.Tests` green
(271 tests). Manual/live verification via a throwaway FlaUI driver against the running app (the
existing screenshot harness can't render a hosted `ContentDialog` — it's not a standalone
`Window`) confirmed: the dialog is a true in-window overlay (top-level window count stayed at 1,
unlike the old `MessageBox`'s separate window); Escape closes it without deleting the phrase; the
Delete button removes the phrase end-to-end. **Not conclusively verified:** whether Escape also
silently fires `MainWindow`'s window-level `Escape → StopCommand` panic-stop `KeyBinding` (a
`MessageBox` was a separate top-level window, so its Escape never reached that binding; an
in-window `ContentDialog` overlay might bubble an unhandled key event up to it). Harmless today
since `StopCommand` no-ops when nothing is playing — worth a closer look before Phase C adds a
dialog that could open while a phrase is playing.

**Rollback:** revert `MainWindow.xaml`, `MainWindow.xaml.cs`, `BoardViewModel.cs`, and
`BoardViewModelTests.cs` (the 4 files this scoped pass touched).

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
([ui-ux-localization-scope.md](../../plans/ui-ux-localization-scope.md) slice 3), not a structural bug
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

## Pass 6 — Phrase tile fixed size + tag overflow indicator (audit F1, expanded) — ✅ SHIPPED

**Status: shipped.** Added 2026-07-12 from direct owner feedback while reviewing the board
screenshots (not a re-run of the audit). Absorbed into the Phase B board rebuild (commit
`41e3a6f`, 2026-07-18) rather than landing as its own Phase 5 turn — this plan doc's status
table just never got updated to say so, which is what the 2026-07-19 doc-review pass below
caught and corrected.

**What actually shipped (Phase B, `41e3a6f`):**
- Fixed tile footprint: `PhraseButtonStyle` sets `Width="148" Height="128"`
  (`Theme/Controls.xaml`).
- Title clamp: **not** `TextTrimming` (confirmed in code review that `TextTrimming` +
  `TextWrapping="Wrap"` doesn't ellipsize a wrapped line in WPF — a real gap, not this plan's
  assumption). Instead a `TitleClampConverter` does real `FormattedText` measurement to
  guarantee ≤2 lines with an ellipsis; `TileTitleStyle`'s `MaxHeight="48"` is a safety net only.
- Tag overflow: **Option A** (view-model-computed), not Option B — `PhraseItemViewModel`
  exposes `VisibleTagChips`/`OverflowTagCount`/`HasOverflowTags`, capped by
  `MaxVisibleTagChips`. Shipped at **1**, not the 2 this plan proposed — tuned down after
  rendering showed 2 short tags + the "+N" chip together clipping past the tile's rounded
  corners.
- Broken-tile warning and the tag strip share the same Grid row, mutually exclusive via a
  `DataTrigger` — so the fixed height holds up in the broken-audio state too, not just the
  tag-count/title-length cases this plan described.

**Bug found and fixed during the 2026-07-19 doc-reconciliation pass:** re-verifying this pass
against a fresh render (owner spotted it directly in the PNG) showed tiles were **still**
rendering at inconsistent heights — a no-tag tile measured ~89.6px of visible fill vs. ~108.8px
for a 3-tag/long-title tile (confirmed via live visual-tree instrumentation, not just the
screenshot). Root cause: the Button's own `Width`/`Height` Setters correctly fix its outer
hit-box (confirmed uniformly 128 at every WrapPanel position), but WPF-UI's `ui:Button` control
template centers its `ContentPresenter` instead of stretching it — so `VerticalContentAlignment
="Stretch"` on `PhraseButtonStyle` was a no-op, and the tile's visible content (the
`PhraseTileFillStyle` Border) sized itself to its own content instead of filling the fixed box,
silently reintroducing the exact F1 defect this pass was meant to eliminate. Fixed by adding an
explicit `Width="148" Height="128"` directly to the tile's content-root `Grid`
(`MainWindow.xaml`, immediately inside the `ui:Button`) — validated live (forcing the same
property change made the visible fill render at exactly 128 for both a no-tag and a 3-tag tile)
before touching the shipped file. See the new
`PhraseTileLayoutTests.Phrase_tiles_render_at_a_uniform_height_regardless_of_tags_or_title`
regression test (`tests/AdaVoice.App.Tests/Screenshots/`), confirmed red without the fix and
green with it.

**Original problem statement (2026-07-12, for context):** the tile's outer container had no
explicit size — height and width both grew with content, so a phrase with 0 tags rendered
visibly shorter than one with 2 tags, and a long title (audit's original F1) rendered taller
still. Resolved as described above — fixed size, view-model-computed tag cap (Option A), title
clamp via converter, plus the content-stretch bug caught and fixed 2026-07-19.

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
4. ✅ Pass 6 (phrase tile fixed size + tag overflow) — done, shipped in Phase B, layout bug
   found and fixed 2026-07-19.
5. ✅ Pass 2b (`ContentDialog` migration) — **fully shipped 2026-07-19**: board delete-confirm
   first, then the 4 SettingsWindow prompts + every other dialog's own confirm via Phase C
   Steps 2–5 (see the Pass 2b section above).
6. Pass 1 and 5 — already done; no implementation turn needed, just recorded here.
