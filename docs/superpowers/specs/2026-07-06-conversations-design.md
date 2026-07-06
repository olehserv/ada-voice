# Conversations — Design Spec

_Date: 2026-07-06. Status: approved (brainstorming). Next: implementation plan._

## Problem

Today a Phrase belongs to exactly one Category (`PhraseEntry.CategoryId`, FR-3 in
[design 01 §4](../../design/01-overview.md#4-confirmed-decisions-canonical)), and the operator
triggers phrases one at a time by hotkey/click. There is no way to group phrases into an ordered
script for a specific call type (e.g. "Cold call intro", "Escalation flow"). Categories can't fill
this role: a phrase already has one category (topic), and a script for a call scenario often reuses
phrases from several categories in a specific order.

**Conversations** is a new entity: an ordered, named list of existing phrases the operator can
activate on the Board to get a filtered, in-order view of just that script, with a visual pointer
tracking roughly where they are in the call.

**Explicitly out of scope for this feature:**
- Auto-play / forced sequencing — the operator still triggers every phrase manually (unchanged
  from decision #9 in [design 01](../../design/01-overview.md), "new trigger stops current phrase").
- Persisting the step pointer across app restarts or conversation deactivation — it's per-call UI
  state, not saved data.
- Drag-and-drop reordering — this project already trimmed drag-and-drop from v1 (design 01 scope
  section); reordering uses move-up/move-down buttons, same as the rest of the app would.
- Combining a Conversation filter with a Category filter at the same time (mutually exclusive).

## Architecture

Conversation is a new domain entity, additive to `Library`, following the same shape as the
existing `Category`/`TagInfo` lists — no new persistence idiom, no new host seam beyond what
`ILibraryHost` already exposes for categories.

```
App
├── ViewModels
│   ├── BoardViewModel        — + Conversation selector, step-pointer state (transient)
│   └── ConversationsViewModel — new, parallel to CategoriesViewModel
├── Views
│   └── ManageConversationsDialog.xaml[.cs] — new, parallel to ManageCategoriesDialog
Core
├── Domain
│   └── Conversation.cs        — new record
└── Storage
    ├── Library.cs             — + List<Conversation> Conversations
    └── LibraryValidator.cs    — + referential-integrity cleanup for PhraseIds
```

`PhraseEntry` is untouched. A phrase doesn't know which conversations reference it — the reference
lives on `Conversation.PhraseIds`, the same direction categories reference nothing back from
phrases either.

## 1. Domain model

```csharp
// src/AdaVoice.Core/Domain/Conversation.cs
public record Conversation(
    string Id,
    string Name,
    IReadOnlyList<string> PhraseIds,   // ordered — index = step number
    int SortOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
```

- Many-to-many: a phrase can appear in multiple conversations (and multiple times is not
  prevented, but the UI won't offer to add a duplicate from the picker).
- Order lives on the `Conversation` (position in `PhraseIds`), not on the phrase — a phrase can be
  step 1 in one conversation and step 4 in another.
- No color/visual identity on `Conversation` itself — tiles keep their existing category-driven
  color; the conversation only changes which tiles are visible and which one is highlighted.

## 2. Storage & migration

- `Library.Version` bumps by one; `Conversations` defaults to an empty list when loading an
  older-schema file — no migration script needed, matching every prior additive schema change
  ([design 04 §schema versioning](../../design/04-data-storage.md)).
- `LibraryValidator` adds one check: every ID in a conversation's `PhraseIds` must reference an
  existing `PhraseEntry.Id`. If a phrase was deleted, its ID is **silently dropped** from any
  conversation's list on load/save — this is quiet cleanup, not a repair-dialog situation, because
  deleting a phrase is a deliberate user action elsewhere (unlike a WAV file going missing
  unexpectedly, which is what the repair dialog is for).
- `BackupService` and `LibraryArchiveService` (export/import) need **no code changes** — both
  already operate on the whole `library.json`, so `Conversations` rides along automatically.

## 3. Board UI integration

- New "Conversation:" selector next to the existing Category dropdown, default "None".
- Selecting a Conversation:
  - Filters `PhrasesView` (the existing `ICollectionView` `BoardViewModel` already uses for
    Category) to just `Conversation.PhraseIds`, in list order.
  - Disables the Category dropdown while active — the two filters are mutually exclusive, not
    combinable.
  - Resets the step pointer to index 0.
- **Step pointer:** transient `BoardViewModel` state, not persisted to `Library`. The tile at the
  current index gets a distinct visual treatment (exact styling is a design-05 detail, not decided
  here — e.g. an accent border). Playing **any** phrase in the active conversation moves the
  pointer to `(played phrase's index) + 1`, clamped to the last index — this tracks the real flow
  of the call rather than enforcing strict order, so an operator can jump ahead if the caller does.
- Switching to "None" or a Category exits Conversation mode and discards the pointer.

## 4. Manage Conversations dialog

Structurally parallel to `ManageCategoriesDialog` / `CategoriesViewModel`:

- `ManageConversationsDialog.xaml[.cs]` + `ConversationsViewModel`.
- List of conversations: Add / Rename / Delete via relay commands over `ILibraryHost`, same
  pattern as categories today. Duplicate names are allowed, matching how Categories don't enforce
  uniqueness either.
- Selecting a conversation shows its ordered phrase list: a searchable picker to **Add** an
  existing phrase, plus **Remove** and **Move up / Move down** per row. No drag-and-drop (see
  scope note above).
- `BoardViewModel.ManageConversations()` opens the dialog and refreshes the Conversation selector
  afterward, mirroring `ManageCategories()`.

`ConversationsViewModel` depends on `ILibraryHost` and the phrase list (read-only, for the picker)
— it does not depend on `BoardViewModel`, matching how `CategoriesViewModel` and `BoardViewModel`
are decoupled today.

## 5. Edge cases

- **Deleting a phrase:** quietly dropped from any conversation's `PhraseIds` (§2).
- **Deleting a conversation:** removes the `Conversation` row only; phrases and categories are
  untouched.
- **Empty conversation** (no phrases, or all removed): Board shows an empty-state message in
  Conversation mode, reusing the existing empty-state visual pattern — no "record into this"
  action, since a conversation has no category to record into.
- **Single-phrase conversation:** pointer logic still holds — `(index + 1)` clamps to the last
  index, no special case.

## 6. Testing

- **Core:** `Conversation` record behavior, `LibraryValidator` cleanup (deleted phrase removed from
  `PhraseIds`), migration default (`Conversations` empty on old-schema load).
- **App:** `BoardViewModel` — selecting a conversation filters+orders `PhrasesView` correctly,
  pointer advances to `played + 1` and clamps at the end, switching to "None"/Category exits mode
  and discards the pointer; `ConversationsViewModel` — add/remove/reorder/save round-trip through a
  fake `ILibraryHost` (same fake `CategoriesViewModel` tests already use).
