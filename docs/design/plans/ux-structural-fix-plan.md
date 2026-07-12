# UX structural fix plan — 2026-07-12

Sized to what [the audit](../audits/ux-layout-style-audit.md) actually found, not the full
5-pass template — several passes have little or nothing to do, and that's noted rather than
padded. Each pass becomes its own Phase 5 implementation turn (one window at a time, stop for
approval after each), per the workflow's critical rules.

## Pass 1 — Modal/dialog resizing stabilization

**Status: no work needed.** All 8 dialogs already use `ResizeMode="NoResize"` with either
`SizeToContent="Height"` + `MaxHeight`, or a fixed size for dialogs with swapping content
states (`RecorderDialog`). Verified by direct read of every dialog's root element during the
audit. Nothing to implement; this pass exists in the plan only to record that it was checked.

## Pass 2 — Fixed footer action bars (audit B1)

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

## Pass 3 — MainWindow layout stability (audit C1)

**Status: investigation, not a code change.** `MinWidth`/`MinHeight` are already set and
correct. There is no `MaxWidth`/`MaxHeight`, and the single shipped layout has never been
verified above the 420×640 default screenshot size.

**Action:** manually resize the real app from 420 px up to a typical 1080p desktop width and
confirm: the search/filter row doesn't wrap or clip, the phrase `WrapPanel` re-flows cleanly,
STOP stays full-width and readable, nothing overlaps. Document the result in this file's
follow-up note (or a short addendum) rather than changing code — if it holds up, the finding is
closed with "verified, no fix needed." If it doesn't hold up, that becomes new pass 3b, sized
to whatever's actually found (not more of the automatic responsive-layout guess).

**Explicitly out of scope for this pass:** building the Full/Docked 720 px responsive layout —
that's a separate, already-owned feature decision
([ui-ux-localization-scope.md](../ui-ux-localization-scope.md) slice 3), not a structural bug
fix. Don't fold it into this UX pass without a separate go-ahead.

**Risk:** none (no code change) unless the manual check surfaces a real problem.

## Pass 4 — Button/action consistency (audit D1, D2)

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

## Pass 5 — Form layout cleanup

**Status: no work needed.** The audit found labels aligned, sections grouped in
`PanelStyle` borders with consistent margins, and no validation-placement problems in any
current form. Nothing to implement. Revisit only if a future form (e.g. a Devices group,
per `handoff.md`'s open follow-ups) is added without following the rules in
[wpf-ux-design-rules.md §4](../wpf-ux-design-rules.md#4-form-ux-rules).

## Suggested implementation order

1. Pass 2 (Settings footer) — quick, isolated, highest user-visible payoff for its size.
2. Pass 4 (button semantics) — quick, no risk, four small edits.
3. Pass 3 (MainWindow verification) — no code risk, but needs a live app run; do it before
   Pass 2b so any real resize bug it finds doesn't get buried under the bigger dialog rewrite.
4. Pass 2b (ContentDialog migration) — last, biggest, needs its own careful review.
5. Pass 1 and 5 — already done; no implementation turn needed, just recorded here.

Waiting for approval before starting Pass 2.
