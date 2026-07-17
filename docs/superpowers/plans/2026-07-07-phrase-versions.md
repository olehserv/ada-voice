# Phrase versions — decision record

_Status: ✅ Shipped (approved 2026-07-07, revised 2026-07-08, smoke-tested). This file was the
implementation plan; it is now compacted to the decisions that outlive the implementation.
The full plan (step-by-step design, call-site analysis, test list) is in git history.
Referenced from code: `PhraseVersion.cs`, `Conversation.UseRandomVersion`._

## Behavior contract (confirmed with the user)

- A phrase always keeps its **primary** recording. Versions are additional alternate takes —
  never a replacement.
- A normal board click **always** plays the primary. No randomization outside a Conversation.
- `Conversation.UseRandomVersion` (default off): when **on**, playing a phrase as a
  conversation step picks uniformly at random from **primary + all versions**; when off, a
  step plays the primary — identical to pre-feature behavior.

## Decisions that still matter

- **Versions window (2026-07-08 revision):** version management moved out of the Edit dialog
  into a dedicated Versions window (`PhraseVersionsDialog`/`PhraseVersionsViewModel`, opened
  via the tile context menu's "Versions…") — a board-like tile grid, primary first, each tile
  playable, versions renamable/deletable inline, plus "Add version (record)…".
- **Id prefix `pv-`** (not `v-`, which Conversation ids already use — keeps ids unambiguous
  in logs/exports). Version WAV: `{phraseId}-{versionId}.wav`.
- **Per-version `GainDb`:** each take is loudness-matched independently, like the primary.
- **Random pick lives in `BoardViewModel`** (injectable `Random` seam for deterministic
  tests); `EngineHost`/`IPlaybackHost` stay version-agnostic — they play whatever file+gain
  they are handed.
- **Recording a version reuses the one recording pipeline** via a
  `_pendingVersionForPhraseId` stash; every path that clears the pending take must clear the
  stash too (a leak would misfile the next unrelated recording as a version — regression-tested).
- **Export strips versions (v1), backups keep them.** Export tells the operator how many
  takes were dropped; import defensively strips `Versions` from foreign archives.
- **Version actions commit eagerly** (add/rename/delete) — one consistent rule, mirroring
  eager phrase delete.

## Still deferred (YAGNI, unchanged)

Version reordering; per-version tags/category; weighted random; version audio in
export/import; version count badge on the tile; undo for rename/delete.
