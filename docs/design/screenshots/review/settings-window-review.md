# SettingsWindow — structural fix review (Pass 2, audit finding B1)

## What changed

`src/AdaVoice.App/SettingsWindow.xaml`: root `Grid` restructured from 2 rows to 3 (TitleBar /
scrollable content / footer). The "Done" button moved out of the content `ScrollViewer`'s
`StackPanel` into the new `Grid.Row="2"` footer, gaining `Margin="{StaticResource
Space.ButtonRow}"` to match the convention already used in `ManageCategoriesDialog.xaml` /
`ManageConversationsDialog.xaml`. No other attribute, binding, command, or panel content
changed. Net diff from the pre-Pass-2 baseline: **3 insertions, 2 deletions**, one file.

Executed via subagent-driven-development (per your request): one implementer subagent did the
structural edit, a first review caught that the implementer's `xstyler` run had silently
reformatted the *entire file* (one-attribute-per-line, alphabetized, + a stray UTF-8 BOM) —
diverging from every sibling dialog's compact formatting and ballooning the diff to 134+/75-.
A fix subagent reverted the reformat/BOM while keeping the structural change; re-review came
back clean.

## Screenshots used

- Before: `docs/design/screenshots/before/dark/settings.png`,
  `docs/design/screenshots/before/light/settings.png` (captured pre-fix, this session)
- After: `docs/design/screenshots/after/settings.png` (dark),
  `docs/design/screenshots/after/settings-light.png` (light)

**Known limitation (already flagged in `before-screenshot-inventory.md`):** the screenshot
harness's fixture content is short enough that it never overflows the `ScrollViewer`'s
`MaxHeight="600"`, so the "Done" button doesn't visually move between before/after — both
screenshots look almost identical. The fix is verified by direct XAML read (the button is now
inside `Grid.Row="2"`, physically outside the scrollable region), not by visual diff. If you
want visual proof, it needs either a manual run with more settings content, or a future fixture
change — out of scope for this pass.

## UX issues fixed

- **B1** (audit): "Done" is no longer reachable only by scrolling — it's now in a fixed footer
  row, consistent with every other dialog in the app.

## Remaining issues

None for this window. (Unrelated, separately-tracked: D1/D2/E2/C1 apply to other windows —
see `docs/design/plans/ux-structural-fix-plan.md`.)

## Files changed

- `src/AdaVoice.App/SettingsWindow.xaml` (commits `95eeafb`, `26b4dd0`)

## Build/test result

- `dotnet build AdaVoice.slnx` — 0 warnings, 0 errors.
- `dotnet test AdaVoice.slnx` (full suite, excl. interactive screenshot tests) — all green
  (App: 238/238, plus Core/Audio/Wasapi/Host unchanged).
- `dotnet test tests/AdaVoice.App.Tests --filter Category=Screenshot` (dark + light) — 14/14
  each, including the regenerated `settings.png`.

## Ready for human review

Yes — structurally verified, tests green, minimal targeted diff. Visual confirmation of the
scroll behavior itself would need a manual run with longer content (see limitation above).
