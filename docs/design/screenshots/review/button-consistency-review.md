# Button/action consistency review (Pass 4, audit D1, D2)

## What changed

Four small, independent attribute edits, no logic changes (commit `c47167c`):

- `ManageCategoriesDialog.xaml` — row "Delete": `Appearance="Secondary"` → `"Danger"`
- `ManageConversationsDialog.xaml` — row "Delete": `Appearance="Secondary"` → `"Danger"`
- `ManageConversationsDialog.xaml` — member "Remove": `Appearance="Secondary"` → `"Danger"`
  (note: my task brief incorrectly assumed this button had no explicit `Appearance` before —
  the reviewer caught that it already did; the fix itself, changing it to `Danger`, is
  unaffected and correct either way)
- `PhraseVersionsDialog.xaml` — "✕" delete-version: `Appearance="Secondary"` → `"Danger"`
- `RecorderDialog.xaml` — "Save" (pending-take state): added `IsDefault="True"`, `Appearance`
  stays `Primary` (Save isn't destructive)

Diff: 4 files, 5 insertions / 5 deletions. Executed the same way as Pass 2 (subagent-driven-
development): implementer + reviewer, no fix round needed this time — the implementer avoided
the whole-file-reformat mistake from Pass 2 by hand-editing only the named attributes.

## Screenshots used

- Before: `docs/design/screenshots/before/dark/{manage-categories,manage-conversations,
  phrase-versions,recorder}.png`
- After: `docs/design/screenshots/after/{manage-categories,manage-conversations,
  phrase-versions,recorder}.png`

Visually confirmed: Delete/Remove/✕ now render in WPF-UI's red `Danger` color, clearly
distinct from the neutral Save/Add/Done/Close buttons in all three dialogs where it's visible.
`RecorderDialog`'s `IsDefault` change is not visually provable from the idle-state screenshot
(the pending-take/Save-form state isn't captured by the fixture) — verified by direct XAML read
instead.

## UX issues fixed

- **D1** (audit): destructive actions (Delete category, Delete conversation, Remove phrase,
  Delete version) are now visually distinct from non-destructive secondary actions.
- **D2** (audit): Recorder's Save button now responds to Enter in the pending-take state.

## Remaining issues

None for these four buttons. (Separately tracked: C1, E2 apply to other windows — see
`docs/design/plans/ux-structural-fix-plan.md`.)

## Files changed

- `src/AdaVoice.App/ManageCategoriesDialog.xaml`
- `src/AdaVoice.App/ManageConversationsDialog.xaml`
- `src/AdaVoice.App/PhraseVersionsDialog.xaml`
- `src/AdaVoice.App/RecorderDialog.xaml`
- (commit `c47167c`)

## Build/test result

- `dotnet build AdaVoice.slnx` — 0 warnings, 0 errors.
- `dotnet test AdaVoice.slnx` (full suite, excl. interactive screenshot tests) — all green
  (App: 238/238).
- Screenshot regeneration for the 4 affected windows — passed, visually confirmed above.

## Ready for human review

Yes — minimal targeted diff, tests green, visually confirmed.
