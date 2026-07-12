# Before-screenshot inventory — 2026-07-12

Reused the existing FlaUI-based harness (`tests/AdaVoice.App.Tests/Screenshots/`) instead of
building a new one — see [SKILL note](../wpf-ux-design-rules.md#11-verification-rules).
Command:

```powershell
$env:ADAVOICE_SCREENSHOTS=1; dotnet test tests/AdaVoice.App.Tests --filter "Category=Screenshot"
$env:ADAVOICE_SCREENSHOT_THEME="Light"; dotnet test tests/AdaVoice.App.Tests --filter "Category=Screenshot"
```

Both runs passed (14/14 each). Output was written to the existing `docs/ui/screenshots/after`
and `after-light` (the harness's fixed location — see `TestPaths.ScreenshotDirectory`, not
modified), then copied verbatim into this workstream's baseline:

- `docs/design/screenshots/before/dark/*.png`
- `docs/design/screenshots/before/light/*.png`

These **are** the current state of the app — captured today, after the 2026-07-11 "Studio
Graphite" redesign, before any change from this UX workstream.

| File | Window/view | State | Known issues visible | Baseline-suitable? |
|---|---|---|---|---|
| `main-board.png` | `MainWindow` | Populated board, engine Live, 4 phrases across 3 categories | Tile row heights vary slightly with title length (F1) — subtle, may not show at this sample size | Yes |
| `settings.png` | `SettingsWindow` | Levels + Behavior + Language&Backup, no scroll triggered at this content length | **Won't show B1** — the fake view-model's content is short enough not to overflow `MaxHeight`. B1 only reproduces with more real-world content (e.g. a longer hotkey-status string or a future 4th section). Noted as a harness limitation, not a false audit finding — confirmed by direct XAML read (`SettingsWindow.xaml:86`). | Yes, with the caveat above |
| `recorder.png` | `RecorderDialog` | Idle state (Record button visible) | None visible in this state; D2 (`Save` missing `IsDefault`) only matters in the take-pending state, not captured here | Yes |
| `manage-categories.png` | `ManageCategoriesDialog` | 3 categories listed + add-row | D1 (Delete uses Secondary, not Danger) visible | Yes |
| `manage-conversations.png` | `ManageConversationsDialog` | 1 conversation, 3 members | D1 (Delete/Remove use Secondary) visible | Yes |
| `phrase-edit.png` | `PhraseEditDialog` | Editing "Warm intro" | None open | Yes |
| `phrase-versions.png` | `PhraseVersionsDialog` | Primary + 1 version tile | D1 (✕ delete uses Secondary) visible | Yes |
| `repair-phrase.png` | `RepairPhraseDialog` | Broken-phrase prompt | None open | Yes |
| `setup-wizard.png` | `SetupWizardWindow` | Step 1 shell | None open | Yes |
| `wizard-1..5-*.png` | Setup wizard steps | Each step in isolation | None open | Yes |

Each file above exists in both `before/dark/` and `before/light/`.

## Gaps for later passes

- **SettingsWindow overflow state** isn't in the screenshot set — the fake data is too short to
  trigger the scroll that exposes B1. When B1 is fixed, capture an *additional* manual or
  fixture-extended screenshot with enough content to prove the footer stays visible; don't rely
  on the existing fixture alone to verify the fix.
- **MainWindow at widths other than the default 480×640** (C1) isn't covered by this harness —
  it always renders at a fixed size. If C1's investigation needs visual proof, that requires a
  manual resize test on the real app, not this harness.
