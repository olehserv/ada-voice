# Interaction-State Gaps — Design Spec

_Date: 2026-07-04. Status: approved (brainstorming). Next: implementation plan._

## Problem

[Design 05 §2](../../design/05-ui-design.md#2-interaction-states-what-she-sees) names the states the
Board should show for five areas; the real app only partially matches. This is slice 2 of
[`docs/plans/ui-ux-localization-scope.md`](../../plans/ui-ux-localization-scope.md), picked up after
slice 1 (Settings window).

| Area | Design 05 target | Today |
|---|---|---|
| Broken phrase | Click opens a repair dialog (re-record / remove) | Dims + sets a `Notice` text only |
| Category (empty) | "No phrases in {category} yet." + record-into-category button | Falls into the generic no-match card |
| Search no-match | Echoes the query + a Clear-search button | Generic "No phrases match" text, no query, no button |
| Recorder | "Processing…" after Stop | No processing indicator — a UI gap where the idle Record button briefly reappears |
| Wizard checks | Each row: spinner → ✓/✗ | All four rows appear at once |

**Descoped to a future slice:** the recorder's live level meter and live "mic dropped mid-recording"
detection. Both need a capability that doesn't exist anywhere in the app today — periodic polling of
the capture stream *while* recording is in progress. This is the same missing capability that got the
Settings window's Devices group deferred in slice 1; it belongs in a future slice alongside those
meters, not bolted onto this one.

**Context from the 2026-07-04 codebase review:** between slice 1 shipping and this slice starting,
a separate full codebase review
([`docs/reviews/2026-07-04-full-codebase-review.md`](../../reviews/2026-07-04-full-codebase-review.md))
and two fix batches landed (`docs/plans/2026-07-04-top10-risk-fixes.md`,
`docs/plans/2026-07-04-next-touch-fixes.md`). Relevant here: `BoardViewModel.StartRecording` /
`StopRecording` / `PreviewTake` are now async off the UI thread with broad
`catch (Exception ex) when (ex is not OutOfMemoryException)` → `Notice` guards (fix M1/M2). `SaveTake`
was **not** included in that pass and is still fully synchronous with no `CanExecute` and no catch —
documented as still-open finding **M15**. This slice's Recorder item closes the `SaveTake` half of
M15; the other M15 items (`OpenBackupFolder`, `CategoriesViewModel` duplicate names) are different
screens and stay open for their own fix.

## Architecture

All five items are View + existing-ViewModel changes. None need a new host seam — `ISetupHost`,
`ISettingsHost`, `ILibraryHost`, `IRecorderHost` are all untouched — because each reuses a capability
that already exists:

- **Repair dialog** → new `RepairPhraseDialog` (View) + `RepairPhraseViewModel`, calling the existing
  `DeleteEntry` and the existing record flow.
- **Category-empty CTA** → a new derived boolean on `BoardViewModel`, a command that starts recording
  and remembers which category to apply after Save, reusing the existing `SetPhraseCategory`.
- **Search Clear** → one button + one command on `BoardViewModel`, splitting the existing single
  `NoMatches` state into two mutually exclusive ones (search-driven vs category-driven).
- **Recorder Processing/SaveTake guard** → a new `IsProcessing` flag bridging the existing async
  `StopRecording` window, plus a `CanExecute` + try/catch on `SaveTake` matching its sibling commands.
- **Wizard spinner** → View-owned animation only (no VM change) — the checks run and complete
  instantly, so there is nothing real to await; the reveal is a fixed-delay `Storyboard`, the same
  recipe already used for the calibration countdown ring.

## 1. Repair dialog (broken phrases)

**Trigger:** clicking a broken phrase currently sets `Notice = "This phrase's audio file is
missing…"` in `BoardViewModel.Play`. Instead it calls a new `_showRepairDialog` delegate (same
injection pattern as `_showEditDialog`/`_confirmDelete`).

**Dialog contents:** phrase title, the "⚠ audio missing" message, and three actions:
- **Re-record** — closes the dialog, calls `DeleteEntry` (nothing to orphan — the WAV is already
  gone), then starts the normal record flow with `NewTitle` pre-filled from the broken entry's title;
  its category and tags are stashed in the same pending-metadata field `SaveTake` reads (§2), so
  `SaveTake` re-applies them via `SetPhraseCategory`/`SetPhraseTags` right after creating the new
  entry.
- **Remove** — calls `DeleteEntry` directly (identical outcome to today's right-click Delete).
- **Cancel** — closes with no change.

**New pieces:** `RepairPhraseDialog.xaml` + `.xaml.cs`, `RepairPhraseViewModel` (title, two commands,
a `Result` the caller reads after `ShowDialog()` — same shape as `PhraseEditViewModel`).

## 2. Category-empty CTA

**New state:** `CategoryIsEmpty` on `BoardViewModel` — true when `SelectedCategoryFilter` is not the
"All categories" sentinel, `SearchText` is blank, and no entry in `Phrases` has that `CategoryId`
(checked against the category itself, before the search filter runs).

**Precedence:** `CategoryIsEmpty` requires `SearchText` blank, and the search-no-match state (§3)
requires `SearchText` non-blank — the two states are mutually exclusive by construction. Whenever
there is search text, the search-no-match card wins.

**UI:** for this specific case, replaces the generic no-match card with "No phrases in {category}
yet." + a **Record into {category}** button. Clicking it calls the existing `StartRecording` flow,
first stashing `SelectedCategoryFilter.Id` into a new `_pendingMetadata` field — a single
`(string? CategoryId, IReadOnlyList<string>? Tags)`-shaped field on `BoardViewModel`, shared with the
repair dialog's Re-record path (§1), since both need to re-apply metadata to whatever `SaveTake`
creates next. `SaveTake` applies `SetPhraseCategory`/`SetPhraseTags` on the newly created entry for
whichever parts of `_pendingMetadata` are non-null, then clears the field entirely regardless of
outcome (success, discard, or the new save failure path in §4) so a stale value can never leak into
an unrelated future save.

## 3. Search Clear button + query echo

The existing `NoMatches` card splits into two mutually exclusive states:
- `CategoryIsEmpty` (§2), when there is no search text.
- A search-specific no-match state, whenever `SearchText` is non-blank and the filtered view is
  empty. Its text becomes `"No phrases match '{SearchText}'"` (the `{query}` echo design 05
  specifies) plus a **Clear search** button (`ClearSearchCommand` → `SearchText = ""`).

The same **Clear search** button also appears inline next to the search box itself once `SearchText`
is non-empty (design 05 shows it as part of the search control, not only the empty state) — a small
`ui:Button`, visible via a binding on `SearchText`'s emptiness.

## 4. Recorder — Processing state + SaveTake guard

- New `[ObservableProperty] bool IsProcessing`, set `true` at the top of `StopRecording()` and
  cleared on every exit path (success, no-signal, exception) before `Notice`/`PendingTake` are set.
  `ShowRecordButton` becomes `!IsRecording && !IsProcessing && !HasPendingTake`, closing the gap
  where the idle Record button currently flashes back into view between `IsRecording` going false
  and `PendingTake` being set.
- XAML: a third slot in the record-area `StackPanel` (alongside the existing "Recording…" and
  pending-take slots) — `TextBlock Text="Processing…"` visible when `IsProcessing`.
- `SaveTake` gains a `CanExecute` (`!string.IsNullOrWhiteSpace(NewTitle)`) and a
  `try/catch (Exception ex) when (ex is not OutOfMemoryException)` around `_recorder.SaveTake(...)`,
  setting `Notice = "Could not save the recording — check disk space and try again."` on failure.
  `PendingTake` is left **set** (not cleared) on this failure so the operator can retry Save or
  Discard, instead of silently losing the take.

## 5. Wizard per-row spinner

Purely cosmetic, View-owned — same recipe as the calibration countdown ring follow-up from the setup
wizard slice (no `EnvironmentChecksStepViewModel` change). In `EnvironmentChecksStepView.xaml`, each
row's `DataTemplate` gets a `Loaded`-triggered `Storyboard`: show a small spinner for a short fixed
delay (~300–500ms, staggered by row index so they don't all pop at once), then reveal the real ✓/✗
content. Re-running `Recheck` re-triggers the same animation, since the `ItemsControl` re-templates
against the new `Checks` list.

## Error handling

- **Repair dialog:** no new failure modes — `DeleteEntry`/`SetPhraseCategory`/`SetPhraseTags` already
  return `null` safely on an unknown id, which cannot happen here since the dialog acts on the entry
  it was opened for. Cancel is a no-op close.
- **Category-empty CTA:** rides on `StartRecording`'s existing try/catch. `_pendingRecordCategoryId`
  is always cleared (success or failure) so it can't leak into an unrelated future save.
- **Search Clear / query echo:** no failure path — pure text/command.
- **Recorder Processing + SaveTake guard:** see §4 — `Notice` set, `PendingTake` preserved on
  failure. The `CanExecute` guard just disables the button; nothing to fail yet.
- **Wizard spinner:** cosmetic only, no failure path to guard.

## Testing

- **Repair dialog:** `Play` on a broken phrase invokes `_showRepairDialog` instead of setting
  `Notice`. New `RepairPhraseViewModelTests` covers Re-record (calls `DeleteEntry`, arms the pending
  category/tags) and Remove (calls `DeleteEntry` only) — mirrors `PhraseEditViewModelTests`.
- **Category-empty CTA:** `CategoryIsEmpty` true/false across its three governing conditions
  (category selected vs "All", search blank vs not, category populated vs not); "Record into
  category" → `SaveTake` → the new entry's `CategoryId` matches.
- **Search Clear:** `ClearSearchCommand` resets `SearchText`; the search-no-match message contains
  the echoed query.
- **Recorder:** `IsProcessing` is true during `StopRecording`'s in-flight window and false after,
  using the same synchronous-await test pattern already used for `StartRecording`/`PreviewTake`;
  `SaveTake`'s `CanExecute` is false for a blank/whitespace title; a new `SaveTakeThrows` knob on
  `FakePlaybackHost` (matching its existing `TryStartRecordingThrows`/`CalibrateThrows` convention —
  this fake is knob-based, not the subclass-per-throw pattern used in the Settings-window tests)
  drives a test that the catch sets the right `Notice` and does **not** clear `PendingTake`.
- **Wizard spinner:** no unit test — cosmetic, View-owned, verified only by manual smoke test (same
  treatment as the calibration countdown ring).
