# Full Codebase Review (pr-review-toolkit) — 2026-07-17

> **Status: all 10 findings fixed same day.** First run of the `pr-review-toolkit` plugin.
> Six specialized agents (code-reviewer, silent-failure-hunter, type-design-analyzer,
> code-simplifier, pr-test-analyzer, comment-analyzer) reviewed the desktop app in parallel
> (`server/` excluded). This was **not** a fresh codebase: the
> [2026-07-04 full review](2026-07-04-full-codebase-review.md) and
> [2026-07-12 security scan](2026-07-12-security-scan.md) had already fixed every Critical/High
> finding, so the agents were told to exclude anything already fixed and focus on code added
> since — Conversations, phrase versions, and the UX-modernization passes. Every finding below
> was verified against current source before being ranked.

Scope: `src/AdaVoice.Core`, `AdaVoice.App`, `AdaVoice.Audio`, `AdaVoice.Audio.Wasapi`,
`AdaVoice.Host`, and `tests/`. `server/` (monetization backend) excluded by design.

**Headline:** no new Critical/High. Layering is compile-enforced and clean (App → Host →
{Core, Audio, Audio.Wasapi}, no seam violations); CPM clean. Findings clustered in the phrase-
version code added since the last review. The top finding was independently surfaced by
**three of the six agents**.

## Findings (fixed, most severe first)

1. **[High impact] Random-version playback could send silence into a live call, and still
   advance the script step.** `BoardViewModel.PickVersion` picked uniformly from primary +
   all versions without excluding a version whose WAV was missing (`BrokenVersionIds`);
   `EngineHost.PlayEntry` returned `void` and only logged a drop, so nothing reached the
   operator, and the conversation step advanced regardless. Fix: `PlayEntry` now returns
   `string?` (mirrors `PreviewEntry`); `PickVersion` excludes broken versions from the pool;
   `Play` surfaces a non-null result as an error toast and only advances the step on success.
   Tests: `Random_version_pick_never_plays_a_broken_version`,
   `PlayEntry_uses_the_version_file_not_the_primary` (+ 2 more).
2. **[Medium] Library export silently dropped every version recording, with no on-screen
   notice.** `ExportLibrary` returned the dropped count only to the log; the App export path
   was `void`. Fix: `ISettingsHost.Export`/`EngineHost.Export` now return the dropped count;
   `BackupSettingsViewModel.Export` shows an info toast when it's non-zero.
3. **[Medium] `PhraseLibraryService.Add` never validated `CategoryId`** (the edit path,
   `SetPhraseCategory`, already did) — a create/edit asymmetry that let a phrase end up under
   a non-existent category. Fix: `Add` falls back to `Category.DefaultId` for an unknown id,
   mirroring the domain's own Uncategorized concept.
4. **[Medium] Version-tile preview swallowed both the returned error and thrown exceptions.**
   `PhraseVersionsViewModel.Play` discarded `PreviewVersion`/`PreviewEntry`'s error string and
   had an empty catch block. Fix: added a `Notified` event (reuses `BoardNotification`), a
   `SnackbarPresenter` in `PhraseVersionsDialog.xaml`, and the code-behind wiring to show it.
5. **[Medium] `AudioEngine.Events` doc misstated its own threading contract** — it claimed
   "raised on the control thread" but omitted that `PhraseChanged` fires on the render thread
   under the mixer lock. Fix: reworded both `AudioEngine.cs` and `EngineEvent.cs`.
6. **[Medium risk] Test gaps in the new version paths**: `PlayEntry`'s version-vs-primary
   file/gain selection was untested (the live-call output path); backups keep version WAVs
   while export strips them, with no test guarding that asymmetry; the version WAV
   write/orphan-move lifecycle was untested on a real data root. Fix: added
   `PlayEntry_uses_the_version_file_not_the_primary`,
   `Backup_and_recovery_round_trips_a_phrase_with_versions` (asserts the zip entry directly),
   `SaveTakeAsVersion_writes_the_version_wav_then_DeletePhraseVersion_orphans_it`.
7. **[Low-Medium, latent] `SaveTakeAsVersion` reported "New version saved" even when nothing
   was saved** (phrase deleted out from under a stashed recording session). Fix: guard on a
   null result — keep the pending take and show an error instead of a false success.
8. **[Low] Blank rename/save left the row diverged from storage with no feedback.**
   `ConversationsViewModel.Rename`/`CategoriesViewModel.Save` correctly refused a blank name
   but didn't revert the field, so the UI showed blank while storage kept the old value. Fix:
   revert to the persisted name on the blank path.
9. **[Low] Read-only-state edits in the Versions/Conversations dialogs were swallowed by the
   binding engine.** A refused write (library in `ReadError`) threw inside a TwoWay-bound
   setter, which WPF catches internally — no error ever surfaced. Fix: added
   `ILibraryHost.IsWritable` (a new read-only seam member, mirroring prior `BrokenVersionIds`/
   `SettingsWarning` additions); the random-version checkbox and version-label textbox now
   gate `IsEnabled` on it.
10. **[Low] Cluster of stale "future feature" comments** describing shipped features as
    unbuilt (`MicPassthrough`, `Drift`, `EngineHost`, `WasapiAudioOptions`, `WasapiDevices`,
    `Phrase`, `SetupWizardViewModel.Completed`). Fix: reworded each to match current code;
    doc-only, no behavior change.

## Minor / backlog (not fixed this round)

- `Settings.Language` can deserialize to `null` (non-nullable prop).
- Migrations/pruning skip the `RecoveredFromBackup` load path (self-heals on next normal load).
- Blank titles/names survive the JSON round-trip (cosmetic).
- Duplicate phrase/version ids not prevented (already triaged as good-enough).
- Simplifications identified in new code (behavior-preserving, not applied): a
  `ClearPendingRecording()` helper for a repeated stash-clear pair in `BoardViewModel`; merging
  `PreviewEntry`/`PreviewVersion` in `EngineHost`; a shared `OrphanAudio` callback; an
  `EditConversation` helper mirroring `EditPhrase`; a `PhraseVersionsViewModel` tile-build
  helper; a redundant triple-scan in `SetPhraseVersionLabel`.
- Prior-review Mediums still open (pre-existing, out of scope for this round): M8 `WavFile`
  clamp-on-save / format-validate-on-load; M16 `ISettingsHost` grab-bag seam split.
- Prior-review Lows still open: `TryDelete` duplicated ×5, duplicate `JsonSerializerOptions`,
  `RequireName`/`RequireTitle` near-duplicate.

## What to learn from this

- **Independent agents converging on the same bug is a strong signal.** Three of the six
  agents (code-reviewer, silent-failure-hunter, pr-test-analyzer) surfaced the random-version
  silent-play issue from different angles (conventions, error-handling, test coverage) without
  seeing each other's output — that convergence is what made it the clear #1, not any single
  agent's severity label.
- **A returned-but-discarded error is the same bug as a swallowed exception.** Findings 1, 4,
  and 7 are all the same shape: a seam already reports failure (a `string?` return, a `null`
  result), but the caller doesn't check it. The fix is almost always "read the value you
  already have," not new plumbing — cheaper than it looks once you notice the pattern.
- **A "never crashes" or "always logs" promise needs re-auditing after every new event type.**
  Finding 5 recurred the same class as the 2026-07-04 review's H10: `PhraseChanged` was added
  to `EngineEvent` without updating the doc that describes the whole event's threading
  contract. When a sum type gains a case, its parent's doc comment is a place to check, not
  just the new case's own doc.
