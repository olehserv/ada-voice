# Conversations — Design Spec (decision record)

_Date: 2026-07-06. Status: ✅ Shipped and user smoke-tested. Compacted to the decisions that
outlive the implementation; the full spec is in git history. The filter-row UI described in
the original spec (category dropdown + conversation selector) was **superseded** by the
2026-07-07 filter-controls redesign (compact menu buttons) — see
[2026-07-07-filter-controls-redesign.md](2026-07-07-filter-controls-redesign.md).
Referenced from code: `Conversation.cs`._

## What it is

A Conversation is an ordered, named list of existing phrases — a call script the operator
activates on the Board to get a filtered, in-order view with a step highlight. Categories
could not fill this role: a phrase has one category (topic), but a script reuses phrases
across categories in a specific order.

## Decisions that still matter

- **Order lives on the Conversation** (`PhraseIds`, index = step), not on the phrase — the
  same phrase can be step 1 in one script and step 4 in another. Many-to-many by design.
- **Step pointer is transient UI state, never persisted** — it's per-call state, not data.
  Playing any phrase in the active conversation moves the pointer to that phrase's index + 1
  (clamped) — it tracks the real flow of the call rather than enforcing strict order.
- **Category and Conversation filters are mutually exclusive**, not combinable.
- **Deleted phrases are silently pruned** from every conversation's `PhraseIds` — quiet
  cleanup, not a repair-dialog case, because deleting a phrase is a deliberate user action
  (unlike a WAV going missing unexpectedly).
- **No visual identity on the Conversation** — tiles keep their category color; the
  conversation only changes visibility and highlight.
- Backup/export needed no changes — `Conversations` rides along inside `library.json`.

## Explicitly out of scope (v1, unchanged)

- Auto-play / forced sequencing — the operator triggers every phrase manually.
- Drag-and-drop reordering — move up/down buttons only (drag-and-drop trimmed project-wide).
- Duplicate-phrase steps are not prevented in the model, but the picker won't offer one.
