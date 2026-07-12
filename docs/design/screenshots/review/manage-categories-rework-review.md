# ManageCategoriesDialog rework review (owner UX feedback, 2026-07-12)

## What changed

Owner feedback after reviewing Pass 2/4 screenshots, six items in
`src/AdaVoice.App/ManageCategoriesDialog.xaml` + `.xaml.cs` (commits `25dcd7a`, `c1ac0fd`):

1. Row height consistency — `Height="36"` on the name `TextBox`, color `ComboBox`, and action
   button(s) in both the per-category row and the add-new-category row.
2. Delete → icon-only "✕" (`ToolTip="Delete"`), matching `PhraseVersionsDialog.xaml`'s pattern.
3. Color dropdown (`ColorItemTemplate`) — hex text removed, swatch-only (bumped 16x16→20x20).
4. Done → `Appearance="Success"` (green).
5. Per-row "Save" button removed — the name `TextBox`'s `LostFocus` and color `ComboBox`'s
   `SelectionChanged` now auto-persist via a new `RowField_Committed` code-behind handler,
   mirroring `SettingsWindow.xaml.cs`'s `DuckSlider_Committed`. `CategoriesViewModel.cs` is
   untouched — only the view's trigger changed, not the persistence logic.
6. Add → `Content="✓"`, `Appearance="Success"` (green).

**Process note — my own mistake, caught and fixed:** my task brief incorrectly told the
implementer that item 4 (Done → green) was "already done" for this file. It wasn't — I'd only
made that change in `SettingsWindow.xaml` earlier. Caught via the regenerated screenshot
(Done was still gray), fixed directly in a follow-up commit, and the reviewer verified the
correction.

## Screenshots used

- Before: `docs/design/screenshots/before/dark/manage-categories.png`
- After: `docs/design/screenshots/after/manage-categories.png`

Visually confirmed: all three row controls flush at the same height, red ✕ delete buttons,
swatch-only color dropdowns (no hex), green ✓ add button, green Done button.

## Remaining issues

None blocking. One noted-but-accepted quirk from the reviewer: the color `ComboBox`'s
`SelectionChanged` fires once on dialog load as its binding initializes, so each existing
category's `Save` runs once redundantly on open — confirmed idempotent/harmless (same values
re-written), and adding a guard would be scope creep beyond what was asked.

## Files changed

- `src/AdaVoice.App/ManageCategoriesDialog.xaml`
- `src/AdaVoice.App/ManageCategoriesDialog.xaml.cs`
- (commits `25dcd7a`, `c1ac0fd`)

## Build/test result

- `dotnet build AdaVoice.slnx` — 0 warnings, 0 errors.
- `dotnet test AdaVoice.slnx` (full suite, excl. interactive screenshot tests) — all green
  (App: 238/238, unchanged — no ViewModel touched).
- Screenshot regeneration — passed, visually confirmed above.

## Ready for human review

Yes.
