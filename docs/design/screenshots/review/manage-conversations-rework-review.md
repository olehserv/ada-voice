# ManageConversationsDialog rework review (owner UX feedback, 2026-07-12)

## What changed

Same owner feedback batch as `ManageCategoriesDialog`, applied to
`src/AdaVoice.App/ManageConversationsDialog.xaml` + `.xaml.cs` (commit `a3c75af`):

- Done → `Appearance="Success"` (green).
- Row height consistency (`Height="36"`, matching the sibling dialog) on the rename row (name
  TextBox + Delete) and the "Add phrase" row (ComboBox + button).
- Member-list "Remove" → icon-only "✕" (`ToolTip="Remove"`). The conversation-level "Delete"
  button (top area) is deliberately unchanged in content/appearance, per the owner's explicit
  instruction to treat the two differently.
- Rename "Save" button removed — the name `TextBox`'s `LostFocus` now auto-persists via a new
  `RowField_Committed` code-behind handler (same pattern as `ManageCategoriesDialog.xaml.cs`).
  `ConversationsViewModel.cs`/`ConversationRowViewModel` untouched.
- New-conversation row: `StackPanel` → `Grid`, so "Add" sits on the same line as its text
  input.
- New-conversation "Add" → `Content="✓"`, `Appearance="Success"`. The unrelated "Add phrase"
  button (member section) is deliberately unchanged.

## Screenshots used

- Before: `docs/design/screenshots/before/dark/manage-conversations.png`
- After: `docs/design/screenshots/after/manage-conversations.png`

Visually confirmed (by the controller, since the reviewer subagent doesn't view images): even
row heights throughout, red "✕" on member Remove, "Delete" unchanged, green "✓" Add on the same
line as the new-conversation input, green Done.

## Remaining issues

None blocking. Reviewer noted one cosmetic-only inconsistency: `Height` attribute ordering
varies slightly between the touched buttons (before vs. after `Content`/`Appearance`) — no
functional effect, not worth a follow-up commit.

## Files changed

- `src/AdaVoice.App/ManageConversationsDialog.xaml`
- `src/AdaVoice.App/ManageConversationsDialog.xaml.cs`
- (commit `a3c75af`)

## Build/test result

- `dotnet build AdaVoice.slnx` — 0 warnings, 0 errors.
- `dotnet test AdaVoice.slnx` (full suite, excl. interactive screenshot tests) — all green
  (App: 238/238, unchanged — no ViewModel touched).
- Screenshot regeneration — passed, visually confirmed above.

## Ready for human review

Yes.
