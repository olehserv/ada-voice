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

**Human review caught a second issue the subagent review missed:** moving the button out of
the `StackPanel` also moved it out from under that panel's `Margin="{StaticResource
Space.DialogContent}"` — `Space.ButtonRow` (`0,16,0,0`) only adds top spacing, so "Done" ended
up flush against the window's bottom-right corner with no padding. Screenshot caught it
immediately once regenerated; a follow-up fix commit (`4c41715`) nests the `ScrollViewer` and
the button as sibling rows of one inner `Grid` that carries `Space.DialogContent` — the same
structure `ManageConversationsDialog.xaml` already uses — so both get the ambient padding.
Lesson: a text-only diff review can verify structure and miss ambient-margin inheritance;
regenerating the actual screenshot is what caught this.

## Screenshots used

- Before: `docs/design/screenshots/before/dark/settings.png`,
  `docs/design/screenshots/before/light/settings.png`
- After: `docs/design/screenshots/after/settings.png` (dark),
  `docs/design/screenshots/after/settings-light.png` (light) — regenerated after the margin fix

**Confirmed separately, not fixed here (out of scope, per owner):** the `after-light/` screenshot
set actually renders in dark colors, not light — `Tokens.Light.xaml`'s values are correct
(`Surface.Window="#F6F7F9"`), so this is a harness bug (the `ADAVOICE_SCREENSHOT_THEME=Light`
theme swap isn't visually taking effect before capture), not a token/design bug. Logged as a
follow-up in `handoff.md`.

## UX issues fixed

- **B1** (audit): "Done" is no longer reachable only by scrolling — it's in a fixed footer row,
  consistent with every other dialog in the app.
- Margin regression introduced by this pass's own first attempt, caught by human screenshot
  review and fixed in the same pass.

## Remaining issues

None for this window. (Unrelated, separately-tracked: D1/D2/E2/C1 apply to other windows —
see `docs/design/plans/ux-structural-fix-plan.md`. The after-light screenshot harness bug is
tracked in `handoff.md`, out of scope for this UX workstream.)

## Files changed

- `src/AdaVoice.App/SettingsWindow.xaml` (commits `95eeafb`, `26b4dd0`, `4c41715`)

## Build/test result

- `dotnet build AdaVoice.slnx` — 0 warnings, 0 errors.
- `dotnet test AdaVoice.slnx` (full suite, excl. interactive screenshot tests) — all green
  (App: 238/238, plus Core/Audio/Wasapi/Host unchanged).
- `dotnet test tests/AdaVoice.App.Tests --filter Category=Screenshot` — passing, including the
  regenerated `settings.png` (dark) confirming proper button spacing.

## Ready for human review

Yes — structurally verified, tests green, minimal targeted diff, and now visually confirmed
correct (button has proper right/bottom spacing in the regenerated screenshot).
