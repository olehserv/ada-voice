# Conversations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add "Conversations" — an ordered, named group of existing phrases for a call script — so the operator can filter the Board to one script at a time and see a highlight for the phrase they're expected to play next.

**Architecture:** `Conversation` is a new domain entity, additive to `Library` (same shape as `Category`/`TagInfo`), reached through the existing `ILibraryHost` seam. The Board gets a second filter (mutually exclusive with Category) and transient per-session step-pointer state; a new `ManageConversationsDialog` (parallel to `ManageCategoriesDialog`) handles CRUD.

**Tech Stack:** .NET / WPF, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), System.Text.Json (reflection-based, camelCase), xUnit.

**Design spec:** [`docs/superpowers/specs/2026-07-06-conversations-design.md`](../specs/2026-07-06-conversations-design.md) — read it for the full rationale. This plan implements it as written, with two implementation-level corrections found while reading the real code (both noted inline where they apply):

1. **No `Library.Version` bump.** The spec says to bump it; the real code never has for any prior additive field (`Tags` was added the same way `Conversations` is added here — a new `List<T>` defaulting to `[]`). Worse, `LibraryArchiveService.Import` hard-rejects any `Version` that doesn't equal its `SupportedVersion` constant — bumping `Library.Version` without also bumping that constant would break every future export/import round-trip. Conversations lands exactly like Tags did: additive, `Version` untouched.
2. **`Conversation` uses `DateTime`, not `DateTimeOffset`**, matching `PhraseEntry.CreatedAt`/`UpdatedAt` — the only other timestamped domain type in the codebase.

## Global Constraints

- No new host seam: everything goes through the existing `ILibraryHost` (same seam `Category` CRUD already uses).
- Additive to `Library` only — no `Version` bump (see above), no migration script; old libraries load with `Conversations = []`.
- A phrase can belong to many conversations; order lives on the `Conversation` (`PhraseIds`), never on `PhraseEntry`.
- Category and Conversation filters on the Board are mutually exclusive — activating one turns the other off.
- The step pointer is transient `BoardViewModel` state — never persisted to `Library`, reset to 0 every time a Conversation is (re)selected.
- Deleting a phrase silently drops it from every conversation's `PhraseIds` — no repair dialog, no error.
- No drag-and-drop anywhere (matches the project's existing v1 scope decision) — reordering is Move up/Move down buttons.
- Follow existing naming/record conventions exactly: `sealed record` with `{ get; init; }` properties (like `Category`/`PhraseEntry`), not positional records.

---

### Task 1: `Conversation` domain type + `Library.Conversations`

**Files:**
- Create: `src/AdaVoice.Core/Domain/Conversation.cs`
- Modify: `src/AdaVoice.Core/Domain/Library.cs`
- Test: `tests/AdaVoice.Core.Tests/ConversationTests.cs`

**Interfaces:**
- Produces: `AdaVoice.Core.Domain.Conversation` (`Id`, `Name`, `PhraseIds: IReadOnlyList<string>`, `SortOrder`, `CreatedAt`, `UpdatedAt`), and `Library.Conversations: List<Conversation>` — both consumed by Task 2 (`PhraseLibraryService`).

- [ ] **Step 1: Write the failing tests**

Create `tests/AdaVoice.Core.Tests/ConversationTests.cs`:

```csharp
using AdaVoice.Core;
using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests;

public class ConversationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-conv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_conversation_round_trips_through_the_repository()
    {
        var repository = new JsonPhraseRepository(_root);
        var library = repository.Load().Library with
        {
            Conversations =
            [
                new Conversation
                {
                    Id = "v-1",
                    Name = "Cold call intro",
                    PhraseIds = ["p-1", "p-2"],
                    SortOrder = 0,
                    CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                },
            ],
        };
        repository.Save(library);

        var reloaded = new JsonPhraseRepository(_root).Load().Library;

        var conversation = Assert.Single(reloaded.Conversations);
        Assert.Equal("v-1", conversation.Id);
        Assert.Equal("Cold call intro", conversation.Name);
        Assert.Equal(["p-1", "p-2"], conversation.PhraseIds);
    }

    [Fact]
    public void An_old_schema_file_with_no_conversations_key_loads_with_an_empty_list()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(AdaVoicePaths.LibraryFile(_root), """
            {
              "version": 1,
              "categories": [{ "id": "c-default", "name": "Uncategorized", "color": "#808080", "sortOrder": 0 }],
              "phrases": []
            }
            """);

        var library = new JsonPhraseRepository(_root).Load().Library;

        Assert.Empty(library.Conversations);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AdaVoice.Core.Tests --filter ConversationTests`
Expected: FAIL — `Conversation` and `Library.Conversations` do not exist yet (compile error).

- [ ] **Step 3: Create the domain type**

Create `src/AdaVoice.Core/Domain/Conversation.cs`:

```csharp
namespace AdaVoice.Core.Domain;

/// <summary>An ordered, named group of existing phrases for a specific call script (design:
/// docs/superpowers/specs/2026-07-06-conversations-design.md). A phrase can belong to more than one
/// conversation; order lives here (in <see cref="PhraseIds"/>), not on the phrase, since the same
/// phrase can be a different step in each conversation that references it.</summary>
public sealed record Conversation
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Phrase ids in call order — index 0 is step one. Every id here must reference an
    /// existing <see cref="PhraseEntry"/>; a deleted phrase is pruned from this list (never left
    /// dangling — see <c>PhraseLibraryService.Delete</c>).</summary>
    public IReadOnlyList<string> PhraseIds { get; init; } = [];

    public int SortOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
```

- [ ] **Step 4: Add the list to `Library`**

Modify `src/AdaVoice.Core/Domain/Library.cs` — add one property after `Tags`:

```csharp
namespace AdaVoice.Core.Domain;

/// <summary>The whole phrase library as stored in <c>library.json</c> (design 04 §1).</summary>
public sealed record Library
{
    public int Version { get; init; } = 1;
    public List<Category> Categories { get; init; } = [];
    public List<PhraseEntry> Phrases { get; init; } = [];

    /// <summary>The tag registry: one colour per tag name. Phrases store tag names; this gives each name
    /// a stable colour. Grows as tags are used (see <c>PhraseLibraryService.SetPhraseTags</c>).</summary>
    public List<TagInfo> Tags { get; init; } = [];

    /// <summary>Ordered phrase scripts for specific call types. Additive field (like <see cref="Tags"/>
    /// before it) — an older library file simply has none, so this defaults to empty rather than
    /// bumping <see cref="Version"/> (design: docs/superpowers/specs/2026-07-06-conversations-design.md §2).</summary>
    public List<Conversation> Conversations { get; init; } = [];
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/AdaVoice.Core.Tests --filter ConversationTests`
Expected: PASS (2 tests)

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.Core/Domain/Conversation.cs src/AdaVoice.Core/Domain/Library.cs tests/AdaVoice.Core.Tests/ConversationTests.cs
git commit -m "feat(core): add the Conversation domain type"
```

---

### Task 2: `PhraseLibraryService` — Conversation CRUD + phrase-delete cleanup

**Files:**
- Modify: `src/AdaVoice.Core/PhraseLibraryService.cs`
- Test: `tests/AdaVoice.Core.Tests/ConversationTests.cs` (append)

**Interfaces:**
- Consumes: `Conversation`, `Library.Conversations` (Task 1).
- Produces (consumed by Task 3's `ILibraryHost`/`EngineHost`):
  - `IReadOnlyList<Conversation> Conversations { get; }`
  - `Conversation AddConversation(string name)`
  - `Conversation? RenameConversation(string id, string name)`
  - `bool DeleteConversation(string id)`
  - `Conversation? SetConversationPhrases(string id, IReadOnlyList<string> phraseIds)`

- [ ] **Step 1: Write the failing tests**

Append to `tests/AdaVoice.Core.Tests/ConversationTests.cs` (inside the `ConversationTests` class, before the closing brace and `Dispose`):

```csharp
    // A fresh service re-reads from disk, so using one to assert proves the change was persisted.
    private PhraseLibraryService NewService() => new(new JsonPhraseRepository(_root));

    [Fact]
    public void AddConversation_creates_persists_and_appends_sort_order()
    {
        var service = NewService();

        var conversation = service.AddConversation("Cold call intro");

        Assert.StartsWith("v-", conversation.Id);
        Assert.Equal("Cold call intro", conversation.Name);
        Assert.Empty(conversation.PhraseIds);
        Assert.Equal(0, conversation.SortOrder);
        Assert.Contains(NewService().Conversations, c => c.Id == conversation.Id);
    }

    [Fact]
    public void AddConversation_blank_name_throws()
    {
        Assert.Throws<ArgumentException>(() => NewService().AddConversation("   "));
    }

    [Fact]
    public void RenameConversation_renames_and_persists_unknown_returns_null()
    {
        var service = NewService();
        var conversation = service.AddConversation("Old");

        var renamed = service.RenameConversation(conversation.Id, "New");

        Assert.Equal("New", renamed!.Name);
        Assert.Null(service.RenameConversation("v-nope", "X"));
        Assert.Equal("New", Assert.Single(NewService().Conversations).Name);
    }

    [Fact]
    public void DeleteConversation_removes_it_but_leaves_phrases_untouched()
    {
        var service = NewService();
        var phrase = service.Add("p", Category.DefaultId, 100, 0, _ => { });
        var conversation = service.AddConversation("Temp");
        service.SetConversationPhrases(conversation.Id, [phrase.Id]);

        Assert.True(service.DeleteConversation(conversation.Id));

        var reloaded = NewService();
        Assert.Empty(reloaded.Conversations);
        Assert.Single(reloaded.Phrases); // untouched
    }

    [Fact]
    public void DeleteConversation_unknown_returns_false()
    {
        Assert.False(NewService().DeleteConversation("v-nope"));
    }

    [Fact]
    public void SetConversationPhrases_replaces_the_ordered_list_and_drops_unknown_ids()
    {
        var service = NewService();
        var p1 = service.Add("one", Category.DefaultId, 100, 0, _ => { });
        var p2 = service.Add("two", Category.DefaultId, 100, 0, _ => { });
        var conversation = service.AddConversation("Script");

        var updated = service.SetConversationPhrases(conversation.Id, [p2.Id, "p-nope", p1.Id]);

        // unknown id dropped, order preserved for the rest
        Assert.Equal([p2.Id, p1.Id], updated!.PhraseIds);
        Assert.Equal([p2.Id, p1.Id], Assert.Single(NewService().Conversations).PhraseIds); // persisted
    }

    [Fact]
    public void SetConversationPhrases_unknown_conversation_returns_null()
    {
        Assert.Null(NewService().SetConversationPhrases("v-nope", []));
    }

    [Fact]
    public void Deleting_a_phrase_prunes_it_from_every_conversation_immediately()
    {
        var service = NewService();
        var p1 = service.Add("one", Category.DefaultId, 100, 0, _ => { });
        var p2 = service.Add("two", Category.DefaultId, 100, 0, _ => { });
        var conversation = service.AddConversation("Script");
        service.SetConversationPhrases(conversation.Id, [p1.Id, p2.Id]);

        service.Delete(p1.Id, (_, _) => { });

        // in-session, without waiting for a reload
        Assert.Equal([p2.Id], service.Conversations.Single().PhraseIds);
        Assert.Equal([p2.Id], NewService().Conversations.Single().PhraseIds); // persisted
    }

    [Fact]
    public void Loading_a_library_prunes_conversation_references_to_phrases_that_no_longer_exist()
    {
        // A hand-edited or merge-imported library where a conversation outlived a phrase it referenced.
        Directory.CreateDirectory(_root);
        File.WriteAllText(AdaVoicePaths.LibraryFile(_root), """
            {
              "version": 1,
              "categories": [{ "id": "c-default", "name": "Uncategorized", "color": "#808080", "sortOrder": 0 }],
              "phrases": [],
              "conversations": [{ "id": "v-1", "name": "Script", "phraseIds": ["p-gone"], "sortOrder": 0,
                                   "createdAt": "2024-01-01T00:00:00Z", "updatedAt": "2024-01-01T00:00:00Z" }]
            }
            """);

        var loaded = NewService(); // Load() should prune it

        Assert.Empty(loaded.Conversations.Single().PhraseIds);
        Assert.Empty(NewService().Conversations.Single().PhraseIds); // persisted, not re-derived every load
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AdaVoice.Core.Tests --filter ConversationTests`
Expected: FAIL — `PhraseLibraryService` has no `Conversations`/`AddConversation`/etc. members yet (compile error).

- [ ] **Step 3: Add the Conversations property**

Modify `src/AdaVoice.Core/PhraseLibraryService.cs` — insert directly below the existing `Tags` property (near line 32; do not duplicate that property, just add this after it):

```csharp
    /// <summary>The conversations (ordered phrase scripts), in sort order.</summary>
    public IReadOnlyList<Conversation> Conversations => _library.Conversations;
```

- [ ] **Step 4: Prune stale conversation references at load time**

Modify `Load()` (near line 64-79) — replace the body's final `if` with a call to a new prune step, run alongside the existing tag migration:

```csharp
    private void Load()
    {
        var result = _repository.Load();
        _library = result.Library;
        LoadStatus = result.Status;
        LoadDetail = result.Detail;
        BrokenPhraseIds = LibraryValidator.FindBrokenPhraseIds(_library, _audioExists);

        // One-time migrations: give a colour to any tag that predates the registry, and drop any
        // conversation reference to a phrase that no longer exists (e.g. a hand-edited or
        // merge-imported library). Gated to a normal, fully-parsed load: ReadError returns an empty
        // in-memory stand-in for a good-but-locked file specifically so it is never overwritten, and
        // RecoveredFromBackup already persists itself. Migrating+saving on either path would defeat
        // that safety.
        if (LoadStatus == LibraryLoadStatus.Loaded)
        {
            var tagsChanged = RegisterTags(_library.Phrases.SelectMany(p => p.Tags));
            var conversationsChanged = PruneUnknownConversationPhrases();
            if (tagsChanged || conversationsChanged)
                _repository.Save(_library);
        }
    }

    /// <summary>Drop any phrase id from a conversation's step list that no longer matches an existing
    /// phrase. Returns true if anything changed. Does not persist — the caller (<see cref="Load"/>)
    /// saves once for both migrations.</summary>
    private bool PruneUnknownConversationPhrases()
    {
        var knownIds = _library.Phrases.Select(p => p.Id).ToHashSet();
        var changed = false;
        for (var i = 0; i < _library.Conversations.Count; i++)
        {
            var conversation = _library.Conversations[i];
            var filtered = conversation.PhraseIds.Where(knownIds.Contains).ToList();
            if (filtered.Count == conversation.PhraseIds.Count)
                continue;

            _library.Conversations[i] = conversation with { PhraseIds = filtered, UpdatedAt = DateTime.UtcNow };
            changed = true;
        }

        return changed;
    }
```

- [ ] **Step 5: Prune immediately when a phrase is deleted**

Modify `Delete` (near line 142-154):

```csharp
    public PhraseEntry? Delete(string phraseId, Action<string, string> orphanAudio)
    {
        EnsureWritable();
        var entry = _library.Phrases.FirstOrDefault(p => p.Id == phraseId);
        if (entry is null)
            return null;

        _library.Phrases.Remove(entry);
        PruneConversationPhrase(phraseId);
        _repository.Save(_library);

        orphanAudio(entry.FileName, "deleted-" + entry.FileName);
        return entry;
    }

    /// <summary>Remove one phrase id from every conversation's step list — a deleted phrase can no
    /// longer be referenced (design: docs/superpowers/specs/2026-07-06-conversations-design.md §2).
    /// Does not persist; the caller saves.</summary>
    private void PruneConversationPhrase(string phraseId)
    {
        for (var i = 0; i < _library.Conversations.Count; i++)
        {
            var conversation = _library.Conversations[i];
            if (!conversation.PhraseIds.Contains(phraseId))
                continue;

            _library.Conversations[i] = conversation with
            {
                PhraseIds = conversation.PhraseIds.Where(id => id != phraseId).ToList(),
                UpdatedAt = DateTime.UtcNow,
            };
        }
    }
```

- [ ] **Step 6: Add the Conversation CRUD methods**

Modify `src/AdaVoice.Core/PhraseLibraryService.cs` — add a new section after `DeleteCategory` (near line 211), before the `// ---- Phrase edits` section:

```csharp
    // ---- Conversations -------------------------------------------------------------------------

    /// <summary>Create a conversation at the end of the list (no phrases yet) and persist. Throws if
    /// the name is blank.</summary>
    public Conversation AddConversation(string name)
    {
        EnsureWritable();
        var now = DateTime.UtcNow;
        var conversation = new Conversation
        {
            Id = "v-" + Guid.NewGuid().ToString("N")[..8],
            Name = RequireName(name),
            PhraseIds = [],
            SortOrder = _library.Conversations.Count,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _library.Conversations.Add(conversation);
        _repository.Save(_library);
        return conversation;
    }

    /// <summary>Rename a conversation. Returns the updated conversation, or null if no conversation
    /// has that id. Throws if the new name is blank.</summary>
    public Conversation? RenameConversation(string id, string name)
    {
        EnsureWritable();
        var index = _library.Conversations.FindIndex(c => c.Id == id);
        if (index < 0)
            return null;

        var updated = _library.Conversations[index] with { Name = RequireName(name), UpdatedAt = DateTime.UtcNow };
        _library.Conversations[index] = updated;
        _repository.Save(_library);
        return updated;
    }

    /// <summary>Delete a conversation. Its phrases are untouched — a conversation only references
    /// them. Returns false if the id is unknown.</summary>
    public bool DeleteConversation(string id)
    {
        EnsureWritable();
        var index = _library.Conversations.FindIndex(c => c.Id == id);
        if (index < 0)
            return false;

        _library.Conversations.RemoveAt(index);
        _repository.Save(_library);
        return true;
    }

    /// <summary>Replace a conversation's ordered phrase list. Unknown phrase ids are silently
    /// dropped — a conversation may only reference phrases that exist, the same invariant a deleted
    /// phrase's cleanup enforces (see <see cref="PruneConversationPhrase"/>). Returns the updated
    /// conversation, or null if no conversation has that id.</summary>
    public Conversation? SetConversationPhrases(string id, IReadOnlyList<string> phraseIds)
    {
        EnsureWritable();
        var index = _library.Conversations.FindIndex(c => c.Id == id);
        if (index < 0)
            return null;

        var knownIds = _library.Phrases.Select(p => p.Id).ToHashSet();
        var filtered = phraseIds.Where(knownIds.Contains).ToList();

        var updated = _library.Conversations[index] with { PhraseIds = filtered, UpdatedAt = DateTime.UtcNow };
        _library.Conversations[index] = updated;
        _repository.Save(_library);
        return updated;
    }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/AdaVoice.Core.Tests --filter ConversationTests`
Expected: PASS (10 tests)

- [ ] **Step 8: Run the full Core test suite**

Run: `dotnet test tests/AdaVoice.Core.Tests`
Expected: PASS (no regressions — `Load()`'s tag-migration behavior is unchanged, just joined by the new prune step)

- [ ] **Step 9: Commit**

```bash
git add src/AdaVoice.Core/PhraseLibraryService.cs tests/AdaVoice.Core.Tests/ConversationTests.cs
git commit -m "feat(core): conversation CRUD + prune deleted-phrase references"
```

---

### Task 3: `ILibraryHost` + `EngineHost` + `FakePlaybackHost`

**Files:**
- Modify: `src/AdaVoice.Host/ILibraryHost.cs`
- Modify: `src/AdaVoice.Host/EngineHost.cs`
- Modify: `tests/AdaVoice.App.Tests/FakePlaybackHost.cs`

**Interfaces:**
- Consumes: `PhraseLibraryService.Conversations`/`AddConversation`/`RenameConversation`/`DeleteConversation`/`SetConversationPhrases` (Task 2).
- Produces (consumed by Task 4's `ConversationsViewModel` and Task 5's `BoardViewModel`): the same four members plus `Conversations`, now on `ILibraryHost` (and its real implementation `EngineHost`, and the test double `FakePlaybackHost`).

This task is pure plumbing — no new business logic (that's already tested in Task 2). Following this codebase's existing precedent (the `AddCategory`/`UpdateCategory`/`DeleteCategory` delegations on `EngineHost` have no dedicated `Host.Tests` coverage either), there is no new test file; the full test suite is the verification.

- [ ] **Step 1: Add the members to the interface**

Modify `src/AdaVoice.Host/ILibraryHost.cs` — insert directly below the existing `Tags` property (near line 21; do not duplicate that property, just add this after it):

```csharp
    /// <summary>The conversations (ordered phrase scripts), in sort order.</summary>
    IReadOnlyList<Conversation> Conversations { get; }
```

Insert directly below the existing `DeleteCategory` method signature (near line 56; do not duplicate it, just add these after it):

```csharp
    /// <summary>Create a conversation (no phrases yet). Throws if the name is blank.</summary>
    Conversation AddConversation(string name);

    /// <summary>Rename a conversation. Returns the updated conversation, or null if the id is unknown.
    /// Throws if the new name is blank.</summary>
    Conversation? RenameConversation(string id, string name);

    /// <summary>Delete a conversation. Its phrases are untouched. Returns false if the id is
    /// unknown.</summary>
    bool DeleteConversation(string id);

    /// <summary>Replace a conversation's ordered phrase list. Unknown phrase ids are dropped. Returns
    /// the updated conversation, or null if the id is unknown.</summary>
    Conversation? SetConversationPhrases(string id, IReadOnlyList<string> phraseIds);
```

- [ ] **Step 2: Run the build to see the expected compile errors**

Run: `dotnet build`
Expected: FAIL — `EngineHost` and `FakePlaybackHost` no longer satisfy `ILibraryHost` (missing member errors).

- [ ] **Step 3: Implement on `EngineHost`**

Modify `src/AdaVoice.Host/EngineHost.cs` — insert directly below the existing `public IReadOnlyList<TagInfo> Tags => _library.Tags;` line (near line 181; do not duplicate it, just add this after it):

```csharp
    public IReadOnlyList<Conversation> Conversations => _library.Conversations;
```

Insert directly below the existing `public bool DeleteCategory(string id) => _library.DeleteCategory(id);` line (near line 205; do not duplicate it, just add these after it):

```csharp
    public Conversation AddConversation(string name) => _library.AddConversation(name);
    public Conversation? RenameConversation(string id, string name) => _library.RenameConversation(id, name);
    public bool DeleteConversation(string id) => _library.DeleteConversation(id);
    public Conversation? SetConversationPhrases(string id, IReadOnlyList<string> phraseIds) =>
        _library.SetConversationPhrases(id, phraseIds);
```

- [ ] **Step 4: Implement on the test double**

Modify `tests/AdaVoice.App.Tests/FakePlaybackHost.cs` — insert directly below the existing `public List<PhraseEntry> Deleted { get; } = [];` line (near line 30; do not duplicate it, just add this after it):

```csharp
    public IReadOnlyList<Conversation> Conversations { get; set; } = [];
```

Insert directly below the existing `DeleteCategory` method (near line 176; do not duplicate it, just add these after it):

```csharp
    public Conversation AddConversation(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("blank", nameof(name));

        var conversation = new Conversation { Id = "v-" + (Conversations.Count + 1), Name = name.Trim() };
        Conversations = [.. Conversations, conversation];
        return conversation;
    }

    public Conversation? RenameConversation(string id, string name)
    {
        var existing = Conversations.FirstOrDefault(c => c.Id == id);
        if (existing is null)
            return null;

        var updated = existing with { Name = name.Trim() };
        Conversations = Conversations.Select(c => c.Id == id ? updated : c).ToList();
        return updated;
    }

    public bool DeleteConversation(string id)
    {
        if (Conversations.All(c => c.Id != id))
            return false;

        Conversations = Conversations.Where(c => c.Id != id).ToList();
        return true;
    }

    public Conversation? SetConversationPhrases(string id, IReadOnlyList<string> phraseIds)
    {
        var existing = Conversations.FirstOrDefault(c => c.Id == id);
        if (existing is null)
            return null;

        var knownIds = Phrases.Select(p => p.Id).ToHashSet();
        var updated = existing with { PhraseIds = phraseIds.Where(knownIds.Contains).ToList() };
        Conversations = Conversations.Select(c => c.Id == id ? updated : c).ToList();
        return updated;
    }
```

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test`
Expected: PASS — everything compiles and all existing tests (Core, Audio, Wasapi, Host, App) still pass.

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.Host/ILibraryHost.cs src/AdaVoice.Host/EngineHost.cs tests/AdaVoice.App.Tests/FakePlaybackHost.cs
git commit -m "feat(host): expose Conversation CRUD through ILibraryHost"
```

---

### Task 4: `ConversationsViewModel` (the "Manage conversations" logic)

**Files:**
- Create: `src/AdaVoice.App/ViewModels/ConversationsViewModel.cs`
- Test: `tests/AdaVoice.App.Tests/ConversationsViewModelTests.cs`

**Interfaces:**
- Consumes: `ILibraryHost.Conversations`/`Phrases`/`AddConversation`/`RenameConversation`/`DeleteConversation`/`SetConversationPhrases` (Task 3).
- Produces (consumed by Task 6's `ManageConversationsDialog.xaml`):
  - `ConversationsViewModel(ILibraryHost library)`
  - `ObservableCollection<ConversationRowViewModel> Rows`
  - `ConversationRowViewModel? SelectedRow` (settable)
  - `bool HasSelection`
  - `string NewName` (settable), `IRelayCommand AddCommand`
  - `IRelayCommand RenameCommand` (param: `ConversationRowViewModel`), `IRelayCommand DeleteCommand` (param: `ConversationRowViewModel`)
  - `ConversationRowViewModel`: `string Id`, `string Name` (settable), `ObservableCollection<ConversationPhraseRowViewModel> Members`, `IReadOnlyList<PhraseEntry> AddablePhrases`, `PhraseEntry? PhraseToAdd` (settable), `IRelayCommand AddPhraseCommand`, `IRelayCommand RemovePhraseCommand` (param: `ConversationPhraseRowViewModel`), `IRelayCommand MoveUpCommand`/`MoveDownCommand` (param: `ConversationPhraseRowViewModel`)
  - `ConversationPhraseRowViewModel(string PhraseId, string Title)` (record)

- [ ] **Step 1: Write the failing tests**

Create `tests/AdaVoice.App.Tests/ConversationsViewModelTests.cs`:

```csharp
using AdaVoice.App.ViewModels;
using AdaVoice.Core.Domain;

namespace AdaVoice.App.Tests;

public class ConversationsViewModelTests
{
    private static FakePlaybackHost HostWithPhrases() => new()
    {
        Phrases = [
            new PhraseEntry { Id = "p-1", Title = "Intro" },
            new PhraseEntry { Id = "p-2", Title = "Pricing" },
        ],
    };

    [Fact]
    public void Rows_come_from_the_host()
    {
        var host = HostWithPhrases();
        host.Conversations = [new Conversation { Id = "v-1", Name = "Cold call" }];

        var vm = new ConversationsViewModel(host);

        Assert.Equal("Cold call", Assert.Single(vm.Rows).Name);
    }

    [Fact]
    public void Add_creates_a_row_and_persists()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Escalation" };

        vm.AddCommand.Execute(null);

        Assert.Contains(vm.Rows, r => r.Name == "Escalation");
        Assert.Contains(host.Conversations, c => c.Name == "Escalation"); // through the seam
        Assert.Equal("", vm.NewName); // input cleared
    }

    [Fact]
    public void Add_ignores_a_blank_name()
    {
        var vm = new ConversationsViewModel(HostWithPhrases()) { NewName = "   " };

        vm.AddCommand.Execute(null);

        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Rename_renames_through_the_seam()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Old" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();

        row.Name = "New";
        vm.RenameCommand.Execute(row);

        Assert.Contains(host.Conversations, c => c.Name == "New");
    }

    [Fact]
    public void Delete_removes_the_row_and_persists()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Temp" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();

        vm.DeleteCommand.Execute(row);

        Assert.Empty(vm.Rows);
        Assert.Empty(host.Conversations);
    }

    [Fact]
    public void A_rows_addable_phrases_exclude_its_current_members()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();

        row.PhraseToAdd = host.Phrases[0]; // Intro
        row.AddPhraseCommand.Execute(null);

        Assert.DoesNotContain(row.AddablePhrases, p => p.Id == "p-1");
        Assert.Contains(row.AddablePhrases, p => p.Id == "p-2");
    }

    [Fact]
    public void AddPhrase_appends_a_member_and_persists_through_the_seam()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();

        row.PhraseToAdd = host.Phrases[0];
        row.AddPhraseCommand.Execute(null);
        row.PhraseToAdd = host.Phrases[1];
        row.AddPhraseCommand.Execute(null);

        Assert.Equal(["p-1", "p-2"], row.Members.Select(m => m.PhraseId));
        Assert.Equal(["p-1", "p-2"], host.Conversations.Single().PhraseIds);
        Assert.Null(row.PhraseToAdd); // reset after adding
    }

    [Fact]
    public void RemovePhrase_drops_a_member_and_persists()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();
        row.PhraseToAdd = host.Phrases[0];
        row.AddPhraseCommand.Execute(null);
        var member = row.Members.Single();

        row.RemovePhraseCommand.Execute(member);

        Assert.Empty(row.Members);
        Assert.Empty(host.Conversations.Single().PhraseIds);
    }

    [Fact]
    public void MoveUp_and_MoveDown_reorder_members_and_persist()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();
        row.PhraseToAdd = host.Phrases[0]; // p-1
        row.AddPhraseCommand.Execute(null);
        row.PhraseToAdd = host.Phrases[1]; // p-2
        row.AddPhraseCommand.Execute(null);

        row.MoveUpCommand.Execute(row.Members[1]); // p-2 up to first

        Assert.Equal(["p-2", "p-1"], row.Members.Select(m => m.PhraseId));
        Assert.Equal(["p-2", "p-1"], host.Conversations.Single().PhraseIds);

        row.MoveDownCommand.Execute(row.Members[0]); // p-2 back down

        Assert.Equal(["p-1", "p-2"], row.Members.Select(m => m.PhraseId));
    }

    [Fact]
    public void MoveUp_at_the_top_and_MoveDown_at_the_bottom_are_no_ops()
    {
        var host = HostWithPhrases();
        var vm = new ConversationsViewModel(host) { NewName = "Script" };
        vm.AddCommand.Execute(null);
        var row = vm.Rows.Single();
        row.PhraseToAdd = host.Phrases[0];
        row.AddPhraseCommand.Execute(null);

        row.MoveUpCommand.Execute(row.Members[0]);
        row.MoveDownCommand.Execute(row.Members[0]);

        Assert.Equal(["p-1"], row.Members.Select(m => m.PhraseId));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --filter ConversationsViewModelTests`
Expected: FAIL — `ConversationsViewModel` does not exist yet (compile error).

- [ ] **Step 3: Implement `ConversationsViewModel`**

Create `src/AdaVoice.App/ViewModels/ConversationsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using AdaVoice.Core.Domain;
using AdaVoice.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AdaVoice.App.ViewModels;

/// <summary>
/// Backs the "Manage conversations" dialog: add/rename/delete a conversation, and edit which phrases
/// it contains and in what order. Each change is written straight through the
/// <see cref="ILibraryHost"/>. Pure (no XAML), so it is unit-testable with a fake host — mirrors
/// <see cref="CategoriesViewModel"/>.
/// </summary>
public partial class ConversationsViewModel : ObservableObject
{
    private readonly ILibraryHost _library;

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ConversationRowViewModel? _selectedRow;

    public ConversationsViewModel(ILibraryHost library)
    {
        _library = library;
        Rows = new ObservableCollection<ConversationRowViewModel>(
            library.Conversations.Select(c => new ConversationRowViewModel(c, library)));
        _selectedRow = Rows.FirstOrDefault();
    }

    /// <summary>One editable row per conversation.</summary>
    public ObservableCollection<ConversationRowViewModel> Rows { get; }

    /// <summary>True once a conversation is selected — the step editor panel shows only then.</summary>
    public bool HasSelection => SelectedRow is not null;

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewName))
            return;

        var conversation = _library.AddConversation(NewName);
        var row = new ConversationRowViewModel(conversation, _library);
        Rows.Add(row);
        SelectedRow = row;
        NewName = "";
    }

    /// <summary>Persist a row's edited name.</summary>
    [RelayCommand]
    private void Rename(ConversationRowViewModel? row)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.Name))
            return;

        _library.RenameConversation(row.Id, row.Name);
    }

    [RelayCommand]
    private void Delete(ConversationRowViewModel? row)
    {
        if (row is null)
            return;

        if (_library.DeleteConversation(row.Id))
        {
            Rows.Remove(row);
            if (SelectedRow == row)
                SelectedRow = Rows.FirstOrDefault();
        }
    }
}

/// <summary>One conversation in the manager: its editable name and its ordered phrase list. Every
/// membership/order change calls <see cref="ILibraryHost.SetConversationPhrases"/> immediately — like
/// <see cref="CategoriesViewModel"/>, there is no separate "Save" step for the step list.</summary>
public partial class ConversationRowViewModel : ObservableObject
{
    private readonly ILibraryHost _library;

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddablePhrases))]
    private PhraseEntry? _phraseToAdd;

    public ConversationRowViewModel(Conversation conversation, ILibraryHost library)
    {
        _library = library;
        Id = conversation.Id;
        _name = conversation.Name;
        Members = new ObservableCollection<ConversationPhraseRowViewModel>(BuildMembers(conversation.PhraseIds));
    }

    /// <summary>The conversation's phrases, in call order.</summary>
    public ObservableCollection<ConversationPhraseRowViewModel> Members { get; }

    /// <summary>Phrases not already in this conversation — the Add-phrase picker's source. Excluding
    /// current members keeps the picker from ever offering a phrase already added (no duplicate
    /// membership through this dialog).</summary>
    public IReadOnlyList<PhraseEntry> AddablePhrases =>
        _library.Phrases.Where(p => Members.All(m => m.PhraseId != p.Id)).ToList();

    [RelayCommand]
    private void AddPhrase()
    {
        if (PhraseToAdd is not { } phrase)
            return;

        Members.Add(new ConversationPhraseRowViewModel(phrase.Id, phrase.Title));
        Persist();
        PhraseToAdd = null;
        OnPropertyChanged(nameof(AddablePhrases));
    }

    [RelayCommand]
    private void RemovePhrase(ConversationPhraseRowViewModel? row)
    {
        if (row is null)
            return;

        Members.Remove(row);
        Persist();
        OnPropertyChanged(nameof(AddablePhrases));
    }

    [RelayCommand]
    private void MoveUp(ConversationPhraseRowViewModel? row)
    {
        if (row is null)
            return;

        var index = Members.IndexOf(row);
        if (index > 0)
        {
            Members.Move(index, index - 1);
            Persist();
        }
    }

    [RelayCommand]
    private void MoveDown(ConversationPhraseRowViewModel? row)
    {
        if (row is null)
            return;

        var index = Members.IndexOf(row);
        if (index >= 0 && index < Members.Count - 1)
        {
            Members.Move(index, index + 1);
            Persist();
        }
    }

    private IEnumerable<ConversationPhraseRowViewModel> BuildMembers(IReadOnlyList<string> phraseIds)
    {
        var titleById = _library.Phrases.ToDictionary(p => p.Id, p => p.Title);
        return phraseIds.Select(id =>
            new ConversationPhraseRowViewModel(id, titleById.TryGetValue(id, out var t) ? t : "(deleted)"));
    }

    private void Persist() => _library.SetConversationPhrases(Id, Members.Select(m => m.PhraseId).ToList());
}

/// <summary>One phrase's row in a conversation's step list.</summary>
public sealed record ConversationPhraseRowViewModel(string PhraseId, string Title);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --filter ConversationsViewModelTests`
Expected: PASS (11 tests)

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.App/ViewModels/ConversationsViewModel.cs tests/AdaVoice.App.Tests/ConversationsViewModelTests.cs
git commit -m "feat(app): ConversationsViewModel — manage-dialog logic for Conversations"
```

---

### Task 5: `BoardViewModel` — Conversation selector, step pointer, mutual exclusivity

**Files:**
- Modify: `src/AdaVoice.App/ViewModels/BoardViewModel.cs`
- Modify: `src/AdaVoice.App/ViewModels/PhraseItemViewModel.cs`
- Test: `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`

**Interfaces:**
- Consumes: `ILibraryHost.Conversations` (Task 3), `ConversationsViewModel` (Task 4, only as the dialog's VM type, injected by the caller in Task 6).
- Produces (consumed by Task 6's `App.xaml.cs`/`MainWindow.xaml.cs` wiring and Task 7's XAML):
  - New `BoardViewModel` ctor param: `Action<ConversationsViewModel>? showManageConversations = null`
  - `IRelayCommand ManageConversationsCommand`
  - `Conversation SelectedConversationFilter` (settable), `IReadOnlyList<Conversation> ConversationFilterOptions`, `static readonly Conversation NoneConversation`
  - `bool IsConversationActive`, `bool CategoryFilterEnabled`, `bool ConversationIsEmpty`
  - `PhraseItemViewModel.IsCurrentStep` (bool), `PhraseItemViewModel.ConversationStepIndex` (int)

- [ ] **Step 1: Write the failing tests**

Add to `tests/AdaVoice.App.Tests/BoardViewModelTests.cs` — first extend the `NewBoard` helper (near line 11-24) to accept the new callback:

```csharp
    private static BoardViewModel NewBoard(
        FakePlaybackHost host,
        Func<PhraseItemViewModel, bool>? confirmDelete = null,
        Func<PhraseEditViewModel, bool>? showEditDialog = null,
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<ConversationsViewModel>? showManageConversations = null,
        Action<SetupWizardViewModel>? showSetupWizard = null,
        ISettingsHost? settingsHost = null,
        Action<SettingsWindowViewModel>? showSettings = null,
        Func<RepairPhraseViewModel, bool>? showRepairDialog = null) =>
        new(host, host, host, host, settingsHost ?? new FakeSettingsHost(), new StatusViewModel(host),
            new SettingsViewModel(new FakeSettingsHost()),
            getActiveHotkey: () => "Pause", confirmDelete: confirmDelete, showEditDialog: showEditDialog,
            showManageCategories: showManageCategories, showManageConversations: showManageConversations,
            showSetupWizard: showSetupWizard, showSettings: showSettings, showRepairDialog: showRepairDialog);
```

Then append these tests to the same file (inside the class):

```csharp
    [Fact]
    public void Selecting_a_conversation_shows_only_its_phrases_in_step_order()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [
                new PhraseEntry { Id = "p-1", Title = "A" },
                new PhraseEntry { Id = "p-2", Title = "B" },
                new PhraseEntry { Id = "p-3", Title = "C" }, // not in the conversation
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-2", "p-1"] }],
        };
        var board = NewBoard(host);

        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        var visible = board.PhrasesView.Cast<PhraseItemViewModel>().Select(p => p.Entry.Id).ToList();
        Assert.Equal(["p-2", "p-1"], visible); // filtered to the conversation, in its order
    }

    [Fact]
    public void Selecting_a_conversation_turns_off_the_category_filter()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings" }],
            Phrases = [new PhraseEntry { Id = "p-1", CategoryId = "c-1" }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"] }],
        };
        var board = NewBoard(host);
        board.SelectedCategoryFilter = new Category { Id = "c-1", Name = "Greetings" };

        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        Assert.Equal(BoardViewModel.AllCategories.Id, board.SelectedCategoryFilter.Id);
        Assert.False(board.CategoryFilterEnabled);
    }

    [Fact]
    public void Selecting_a_specific_category_turns_off_an_active_conversation()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Greetings" }],
            Phrases = [new PhraseEntry { Id = "p-1", CategoryId = "c-1" }],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"] }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.SelectedCategoryFilter = host.Categories[0];

        Assert.False(board.IsConversationActive);
        Assert.Equal(BoardViewModel.NoneConversation.Id, board.SelectedConversationFilter.Id);
    }

    [Fact]
    public void Playing_a_phrase_in_the_active_conversation_highlights_the_next_step()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav" },
                new PhraseEntry { Id = "p-2", FileName = "p-2.wav" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1", "p-2"] }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");
        Assert.True(board.Phrases.Single(p => p.Entry.Id == "p-1").IsCurrentStep); // starts at step 0

        board.PlayCommand.Execute(board.Phrases.Single(p => p.Entry.Id == "p-1"));

        Assert.False(board.Phrases.Single(p => p.Entry.Id == "p-1").IsCurrentStep);
        Assert.True(board.Phrases.Single(p => p.Entry.Id == "p-2").IsCurrentStep);
    }

    [Fact]
    public void Playing_an_out_of_order_phrase_jumps_the_pointer_to_just_after_it()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav" },
                new PhraseEntry { Id = "p-2", FileName = "p-2.wav" },
                new PhraseEntry { Id = "p-3", FileName = "p-3.wav" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1", "p-2", "p-3"] }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.PlayCommand.Execute(board.Phrases.Single(p => p.Entry.Id == "p-3")); // caller jumped ahead

        Assert.False(board.Phrases.Any(p => p.IsCurrentStep)); // past the last step — nothing highlighted
    }

    [Fact]
    public void Reselecting_a_conversation_resets_the_step_pointer_to_the_first_phrase()
    {
        var host = new FakePlaybackHost
        {
            State = EngineState.Live,
            Phrases = [
                new PhraseEntry { Id = "p-1", FileName = "p-1.wav" },
                new PhraseEntry { Id = "p-2", FileName = "p-2.wav" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1", "p-2"] }],
        };
        var board = NewBoard(host);
        var conversation = board.ConversationFilterOptions.Single(c => c.Id == "v-1");
        board.SelectedConversationFilter = conversation;
        board.PlayCommand.Execute(board.Phrases.Single(p => p.Entry.Id == "p-1")); // pointer now at p-2

        board.SelectedConversationFilter = BoardViewModel.NoneConversation;
        board.SelectedConversationFilter = conversation; // re-select the same conversation

        Assert.True(board.Phrases.Single(p => p.Entry.Id == "p-1").IsCurrentStep); // back to step 0
    }

    [Fact]
    public void Switching_to_none_exits_conversation_mode_and_shows_every_phrase()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [
                new PhraseEntry { Id = "p-1" },
                new PhraseEntry { Id = "p-2" },
            ],
            Conversations = [new Conversation { Id = "v-1", Name = "Script", PhraseIds = ["p-1"] }],
        };
        var board = NewBoard(host);
        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        board.SelectedConversationFilter = BoardViewModel.NoneConversation;

        Assert.Equal(2, board.PhrasesView.Cast<PhraseItemViewModel>().Count());
        Assert.True(board.CategoryFilterEnabled);
    }

    [Fact]
    public void ManageConversations_shows_the_dialog_and_refreshes_the_filter_options()
    {
        var host = new FakePlaybackHost();
        ConversationsViewModel? shown = null;
        var board = NewBoard(host, showManageConversations: vm => shown = vm);

        host.Conversations = [new Conversation { Id = "v-new", Name = "Added mid-dialog" }];
        board.ManageConversationsCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.Contains(board.ConversationFilterOptions, c => c.Id == "v-new");
        Assert.Equal(BoardViewModel.NoneConversation.Id, board.SelectedConversationFilter.Id);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --filter BoardViewModelTests`
Expected: FAIL — `BoardViewModel` has no `Conversations`/`SelectedConversationFilter`/etc. members yet (compile error).

- [ ] **Step 3: Add the ctor param and field**

Modify `src/AdaVoice.App/ViewModels/BoardViewModel.cs` — add the field near `_showManageCategories` (line 39):

```csharp
    private readonly Action<CategoriesViewModel> _showManageCategories;
    private readonly Action<ConversationsViewModel> _showManageConversations;
```

Add the ctor param near `showManageCategories` (line 82) and its default assignment near line 101:

```csharp
        Action<CategoriesViewModel>? showManageCategories = null,
        Action<ConversationsViewModel>? showManageConversations = null,
```

```csharp
        _showManageCategories = showManageCategories ?? (_ => { }); // default: no-op (unit tests)
        _showManageConversations = showManageConversations ?? (_ => { }); // default: no-op (unit tests)
```

- [ ] **Step 4: Add the selector state, sentinel, and derived properties**

Modify `src/AdaVoice.App/ViewModels/BoardViewModel.cs` — insert this new observable property directly below the existing `_selectedCategoryFilter` field (line 72-73; do not duplicate that field, just add this one after it):

```csharp
    /// <summary>The conversation to show, or the "None" sentinel for no conversation filter. Mutually
    /// exclusive with the category filter (design: docs/superpowers/specs/2026-07-06-conversations-design.md §3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConversationActive))]
    [NotifyPropertyChangedFor(nameof(CategoryFilterEnabled))]
    private Conversation _selectedConversationFilter;
```

Insert these private tracking fields directly below the existing `_pendingMetadata` field (line 213):

```csharp
    /// <summary>The active conversation's phrase ids in step order, or null when no conversation is
    /// active. A <c>List</c> (not <see cref="IReadOnlyList{T}"/>) so <see cref="AdvanceStepFor"/> can
    /// use <c>IndexOf</c>.</summary>
    private List<string>? _activeConversationPhraseIds;

    private HashSet<string>? _activeConversationPhraseIdSet;
    private int _currentStepIndex;
```

Insert this sentinel directly below the existing `AllCategories` sentinel (line 140):

```csharp
    /// <summary>Sentinel "no conversation" option for the filter dropdown (blank id = no filter).</summary>
    public static readonly Conversation NoneConversation = new() { Id = "", Name = "None" };
```

Insert these derived properties directly below the existing `CategoryFilterOptions` property (line 173; do not duplicate that property, just add these after it):

```csharp
    /// <summary>"None" followed by the real conversations — the filter dropdown's items. Rebuilt after
    /// the conversation manager runs.</summary>
    public IReadOnlyList<Conversation> ConversationFilterOptions { get; private set; }

    /// <summary>True while a conversation filter is active.</summary>
    public bool IsConversationActive => !string.IsNullOrEmpty(SelectedConversationFilter?.Id);

    /// <summary>The Category filter is disabled while a Conversation is active — the two are mutually
    /// exclusive.</summary>
    public bool CategoryFilterEnabled => !IsConversationActive;

    /// <summary>A conversation is active, no search is active, and none of its phrases are visible on
    /// the board (it has none, or they were all removed from it) — the empty-state card for
    /// Conversation mode. Mutually exclusive with <see cref="CategoryIsEmpty"/> by construction: that
    /// one requires <see cref="EffectiveCategoryId"/> non-null, which is never true while a conversation
    /// is active (the category filter is forced to "All").</summary>
    public bool ConversationIsEmpty => IsConversationActive
        && string.IsNullOrWhiteSpace(SearchText)
        && !Phrases.Any(p => _activeConversationPhraseIdSet!.Contains(p.Entry.Id));
```

- [ ] **Step 5: Initialize the new state in the constructor**

Modify the constructor — add after the existing category filter setup (line 120-122):

```csharp
        // "All categories" + the real categories drive the filter dropdown; default to All.
        CategoryFilterOptions = [AllCategories, .. library.Categories];
        _selectedCategoryFilter = AllCategories;

        // "None" + the real conversations drive the filter dropdown; default to None.
        ConversationFilterOptions = [NoneConversation, .. library.Conversations];
        _selectedConversationFilter = NoneConversation;
```

Update the `PhrasesView.Filter` delegate (line 126) to also respect an active conversation:

```csharp
        PhrasesView = CollectionViewSource.GetDefaultView(Phrases);
        PhrasesView.Filter = o => o is PhraseItemViewModel p
            && Matches(p.Entry, SearchText, EffectiveCategoryId)
            && (_activeConversationPhraseIdSet is null || _activeConversationPhraseIdSet.Contains(p.Entry.Id));
```

Update the `Phrases.CollectionChanged` handler (line 128-135) to also notify `ConversationIsEmpty`:

```csharp
        Phrases.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPhrases));
            OnPropertyChanged(nameof(CategoryIsEmpty));
            OnPropertyChanged(nameof(SearchNoMatch));
            OnPropertyChanged(nameof(HasMatches));
            OnPropertyChanged(nameof(ConversationIsEmpty));
        };
```

- [ ] **Step 6: Add the mutual-exclusivity + step-pointer logic**

Replace the existing `OnSelectedCategoryFilterChanged` one-liner and the existing `RefreshFilter` method (both near line 259-268) with the code below — this replaces those two, and adds three new members (`OnSelectedConversationFilterChanged`, `UpdateCurrentStepHighlight`, `AdvanceStepFor`) after them. `OnSearchTextChanged` (shown first) is unchanged — it's included only so you can see exactly where the replacement starts; do not duplicate it:

```csharp
    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedCategoryFilterChanged(Category value)
    {
        // Picking a specific category turns Conversation mode off — the two filters are mutually
        // exclusive. Picking "All categories" (blank id) is not a category choice, so it never does
        // (this also guards against the recursive call OnSelectedConversationFilterChanged makes below).
        if (!string.IsNullOrEmpty(value?.Id) && IsConversationActive)
            SelectedConversationFilter = NoneConversation;

        RefreshFilter();
    }

    partial void OnSelectedConversationFilterChanged(Conversation value)
    {
        if (!string.IsNullOrEmpty(value?.Id))
        {
            // Activating a Conversation turns off the Category filter — mutually exclusive.
            if (!string.IsNullOrEmpty(SelectedCategoryFilter?.Id))
                SelectedCategoryFilter = AllCategories;

            _activeConversationPhraseIds = value.PhraseIds.ToList();
            _activeConversationPhraseIdSet = _activeConversationPhraseIds.ToHashSet();
            _currentStepIndex = 0;

            var indexById = _activeConversationPhraseIds
                .Select((id, index) => (id, index))
                .ToDictionary(t => t.id, t => t.index);
            foreach (var item in Phrases)
                item.ConversationStepIndex = indexById.TryGetValue(item.Entry.Id, out var index) ? index : int.MaxValue;

            PhrasesView.SortDescriptions.Clear();
            PhrasesView.SortDescriptions.Add(
                new SortDescription(nameof(PhraseItemViewModel.ConversationStepIndex), ListSortDirection.Ascending));
        }
        else
        {
            _activeConversationPhraseIds = null;
            _activeConversationPhraseIdSet = null;
            PhrasesView.SortDescriptions.Clear();
        }

        UpdateCurrentStepHighlight();
        RefreshFilter();
    }

    private void RefreshFilter()
    {
        PhrasesView.Refresh();
        OnPropertyChanged(nameof(CategoryIsEmpty));
        OnPropertyChanged(nameof(SearchNoMatch));
        OnPropertyChanged(nameof(HasMatches));
        OnPropertyChanged(nameof(ConversationIsEmpty));
    }

    /// <summary>Highlight the tile at the current step — the one the operator is expected to play
    /// next. Past the last step (the script is done), nothing is highlighted.</summary>
    private void UpdateCurrentStepHighlight()
    {
        var currentId = _activeConversationPhraseIds is { } order && _currentStepIndex < order.Count
            ? order[_currentStepIndex]
            : null;

        foreach (var item in Phrases)
            item.IsCurrentStep = currentId is not null && item.Entry.Id == currentId;
    }

    /// <summary>Move the step pointer to just after whatever was actually played — not necessarily the
    /// step it was pointing at, so an operator who follows the caller out of order still gets a
    /// sensible "what's next" highlight (design: docs/superpowers/specs/2026-07-06-conversations-design.md §3).</summary>
    private void AdvanceStepFor(string playedPhraseId)
    {
        if (_activeConversationPhraseIds is not { } order)
            return;

        var index = order.IndexOf(playedPhraseId);
        if (index < 0)
            return;

        _currentStepIndex = index + 1;
        UpdateCurrentStepHighlight();
    }
```

(`using System.ComponentModel;` for `ListSortDirection` and `System.Windows.Data` for `SortDescription` are already imported at the top of this file — line 2-3.)

- [ ] **Step 7: Advance the pointer when a phrase actually plays**

Modify `Play` (line 296-331) — add the advance call right after the real play:

```csharp
        Notice = null;
        _playback.PlayEntry(item.Entry);
        if (IsConversationActive)
            AdvanceStepFor(item.Entry.Id);
```

- [ ] **Step 8: Add the ManageConversations command**

Modify `BoardViewModel.cs` — insert this new method directly below the existing `ManageCategories` method (line 231-240; do not duplicate that method, just add this one after it):

```csharp
    /// <summary>Open the conversation manager; when it closes, rebuild the filter dropdown
    /// (conversations may have changed) and reset the filter to "None".</summary>
    [RelayCommand]
    private void ManageConversations()
    {
        _showManageConversations(new ConversationsViewModel(_library));

        ConversationFilterOptions = [NoneConversation, .. _library.Conversations];
        OnPropertyChanged(nameof(ConversationFilterOptions));
        SelectedConversationFilter = NoneConversation; // also refreshes the filter
    }
```

- [ ] **Step 9: Add the new `PhraseItemViewModel` properties**

Modify `src/AdaVoice.App/ViewModels/PhraseItemViewModel.cs` — insert directly below the existing `_isBroken` field (line 29; do not duplicate that field, just add these after it):

```csharp
    /// <summary>True when this phrase is the active conversation's next expected step — the board
    /// gives it a highlight ring. Set by <c>BoardViewModel.UpdateCurrentStepHighlight</c>.</summary>
    [ObservableProperty]
    private bool _isCurrentStep;

    /// <summary>Sort key while a conversation is active: this phrase's position in the conversation's
    /// step order, or <see cref="int.MaxValue"/> if it isn't a member (irrelevant — the board's filter
    /// already hides those). Ignored (left at its default) when no conversation is active.</summary>
    [ObservableProperty]
    private int _conversationStepIndex;
```

- [ ] **Step 10: Run the tests to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --filter "BoardViewModelTests|ConversationsViewModelTests"`
Expected: PASS (all Board and Conversations tests, including the 8 new Board tests)

- [ ] **Step 11: Run the full test suite**

Run: `dotnet test`
Expected: PASS (no regressions — every existing Board test still uses the same `NewBoard` helper, now with one more optional parameter)

- [ ] **Step 12: Commit**

```bash
git add src/AdaVoice.App/ViewModels/BoardViewModel.cs src/AdaVoice.App/ViewModels/PhraseItemViewModel.cs tests/AdaVoice.App.Tests/BoardViewModelTests.cs
git commit -m "feat(app): Board conversation filter, mutual exclusivity, step pointer"
```

---

### Task 6: `ManageConversationsDialog` + window wiring

**Files:**
- Create: `src/AdaVoice.App/ManageConversationsDialog.xaml`
- Create: `src/AdaVoice.App/ManageConversationsDialog.xaml.cs`
- Modify: `src/AdaVoice.App/MainWindow.xaml.cs`
- Modify: `src/AdaVoice.App/App.xaml.cs`

**Interfaces:**
- Consumes: `ConversationsViewModel` (Task 4), `BoardViewModel`'s `showManageConversations` ctor param (Task 5).
- Produces: `MainWindow.ShowManageConversations(ConversationsViewModel)` — wired into `App.xaml.cs`'s `BoardViewModel` construction.

This is WPF window/XAML — no automated test is possible (the existing `ManageCategoriesDialog` has none either). Verification is manual, listed in Step 4.

- [ ] **Step 1: Create the dialog's code-behind**

Create `src/AdaVoice.App/ManageConversationsDialog.xaml.cs`:

```csharp
using System.Windows;

namespace AdaVoice.App;

/// <summary>Modal conversation manager. Its <c>DataContext</c> is a <c>ConversationsViewModel</c>;
/// every change (add/rename/delete a conversation, add/remove/reorder a phrase) is persisted live by
/// that view-model, so closing needs no save step.</summary>
public partial class ManageConversationsDialog : Window
{
    public ManageConversationsDialog() => InitializeComponent();
}
```

- [ ] **Step 2: Create the dialog's XAML**

Create `src/AdaVoice.App/ManageConversationsDialog.xaml`:

```xml
<Window x:Class="AdaVoice.App.ManageConversationsDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
        Title="Manage conversations"
        Width="560" SizeToContent="Height" MaxHeight="620"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize" ShowInTaskbar="False"
        Background="{StaticResource Surface.Window}"
        TextElement.Foreground="{StaticResource Text.Primary}"
        FontFamily="Segoe UI Variable, Segoe UI" FontSize="14">
    <Grid Margin="16">
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="180" />
            <ColumnDefinition Width="16" />
            <ColumnDefinition Width="*" />
        </Grid.ColumnDefinitions>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Left: the conversation list -->
        <ListBox Grid.Column="0" Grid.Row="0" MaxHeight="480"
                  ItemsSource="{Binding Rows}"
                  SelectedItem="{Binding SelectedRow}"
                  DisplayMemberPath="Name" />

        <StackPanel Grid.Column="0" Grid.Row="1" Margin="0,8,0,0">
            <ui:TextBox PlaceholderText="New conversation name"
                        Text="{Binding NewName, UpdateSourceTrigger=PropertyChanged}" />
            <ui:Button Content="Add" Appearance="Primary" Margin="0,8,0,0"
                       Command="{Binding AddCommand}" />
        </StackPanel>

        <!-- Right: the selected conversation's steps -->
        <Border Grid.Column="2" Grid.Row="0"
                Visibility="{Binding HasSelection, Converter={StaticResource BoolToVis}}">
            <StackPanel DataContext="{Binding SelectedRow}">
                <Grid Margin="0,0,0,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <TextBox Grid.Column="0" VerticalAlignment="Center"
                             Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}" />
                    <ui:Button Grid.Column="1" Content="Save" Appearance="Secondary" Margin="8,0,0,0"
                               Command="{Binding DataContext.RenameCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                               CommandParameter="{Binding}" />
                    <ui:Button Grid.Column="2" Content="Delete" Appearance="Secondary" Margin="8,0,0,0"
                               Command="{Binding DataContext.DeleteCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                               CommandParameter="{Binding}" />
                </Grid>

                <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="320">
                    <ItemsControl ItemsSource="{Binding Members}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid Margin="0,0,0,6">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*" />
                                        <ColumnDefinition Width="Auto" />
                                        <ColumnDefinition Width="Auto" />
                                        <ColumnDefinition Width="Auto" />
                                    </Grid.ColumnDefinitions>
                                    <TextBlock Grid.Column="0" Text="{Binding Title}" VerticalAlignment="Center" />
                                    <ui:Button Grid.Column="1" Content="↑" Margin="4,0,0,0" Appearance="Secondary"
                                               Command="{Binding DataContext.MoveUpCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                               CommandParameter="{Binding}" />
                                    <ui:Button Grid.Column="2" Content="↓" Margin="4,0,0,0" Appearance="Secondary"
                                               Command="{Binding DataContext.MoveDownCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                               CommandParameter="{Binding}" />
                                    <ui:Button Grid.Column="3" Content="Remove" Margin="4,0,0,0" Appearance="Secondary"
                                               Command="{Binding DataContext.RemovePhraseCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                               CommandParameter="{Binding}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>

                <Grid Margin="0,8,0,0">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <ComboBox Grid.Column="0" ItemsSource="{Binding AddablePhrases}"
                              SelectedItem="{Binding PhraseToAdd}" DisplayMemberPath="Title" />
                    <ui:Button Grid.Column="1" Content="Add phrase" Margin="8,0,0,0"
                               Command="{Binding AddPhraseCommand}" />
                </Grid>
            </StackPanel>
        </Border>

        <ui:Button Grid.Column="2" Grid.Row="1" Content="Done" Appearance="Secondary" IsCancel="True"
                   HorizontalAlignment="Right" Margin="0,16,0,0" />
    </Grid>
</Window>
```

- [ ] **Step 3: Wire the dialog into `MainWindow` and `App`**

Modify `src/AdaVoice.App/MainWindow.xaml.cs` — add after `ShowManageCategories` (near line 95-96):

```csharp
    /// <summary>Show the modal category manager (changes persist live, so nothing is returned).</summary>
    public void ShowManageCategories(CategoriesViewModel categories) =>
        new ManageCategoriesDialog { DataContext = categories, Owner = this }.ShowDialog();

    /// <summary>Show the modal conversation manager (changes persist live, so nothing is returned).</summary>
    public void ShowManageConversations(ConversationsViewModel conversations) =>
        new ManageConversationsDialog { DataContext = conversations, Owner = this }.ShowDialog();
```

Modify `src/AdaVoice.App/App.xaml.cs` — add one line to the `BoardViewModel` construction (near line 65):

```csharp
            showManageCategories: window.ShowManageCategories,
            showManageConversations: window.ShowManageConversations,
```

- [ ] **Step 4: Manual verification**

Run: `dotnet build` (confirm the new XAML compiles — WPF XAML errors only surface at build time)

Then run the app (see the project's `run` skill or launch `src/AdaVoice.App`) and:
1. Click "Conversations…" (added to the Board in Task 7 — if Task 7 isn't done yet, skip to Task 7 first and come back, or invoke it directly via a temporary test hook).
2. Add a conversation, rename it, add two phrases to it, reorder them with ↑/↓, remove one, delete the conversation.
3. Confirm nothing throws and the dialog's "Done" button closes it.

- [ ] **Step 5: Commit**

```bash
git add src/AdaVoice.App/ManageConversationsDialog.xaml src/AdaVoice.App/ManageConversationsDialog.xaml.cs src/AdaVoice.App/MainWindow.xaml.cs src/AdaVoice.App/App.xaml.cs
git commit -m "feat(app): ManageConversationsDialog + window wiring"
```

---

### Task 7: Board UI — conversation selector, tile highlight, empty state

**Files:**
- Modify: `src/AdaVoice.App/MainWindow.xaml`
- Modify: `src/AdaVoice.App/Theme/Controls.xaml`

**Interfaces:**
- Consumes: `BoardViewModel.ConversationFilterOptions`/`SelectedConversationFilter`/`CategoryFilterEnabled`/`ConversationIsEmpty`/`ManageConversationsCommand` (Task 5), `PhraseItemViewModel.IsCurrentStep` (Task 5).

XAML-only — no automated test. Verification is manual (Step 4), same as Task 6.

- [ ] **Step 1: Add the conversation selector next to the category filter**

Modify `src/AdaVoice.App/MainWindow.xaml` — the search/filter row currently has 4 columns (lines 74-95); expand to 6 and add the conversation controls:

```xml
        <!-- Search box + category filter + conversation filter — hidden until the board has at least one phrase -->
        <Grid Grid.Row="0" Margin="0,0,0,8"
              Visibility="{Binding HasPhrases, Converter={StaticResource BoolToVis}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <ui:TextBox Grid.Column="0" PlaceholderText="Search title or tags…"
                        Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}" />
            <ui:Button Grid.Column="1" Margin="4,0,0,0" Appearance="Secondary" Content="✕"
                       Command="{Binding ClearSearchCommand}" ToolTip="Clear search"
                       Visibility="{Binding HasSearchText, Converter={StaticResource BoolToVis}}" />
            <ComboBox Grid.Column="2" Margin="8,0,0,0" MinWidth="150" VerticalAlignment="Center"
                      ItemsSource="{Binding CategoryFilterOptions}"
                      SelectedItem="{Binding SelectedCategoryFilter}"
                      IsEnabled="{Binding CategoryFilterEnabled}"
                      DisplayMemberPath="Name" />
            <ui:Button Grid.Column="3" Margin="8,0,0,0" Appearance="Secondary"
                       Content="Categories…" Command="{Binding ManageCategoriesCommand}"
                       ToolTip="Add, rename, or delete categories" />
            <ComboBox Grid.Column="4" Margin="8,0,0,0" MinWidth="150" VerticalAlignment="Center"
                      ItemsSource="{Binding ConversationFilterOptions}"
                      SelectedItem="{Binding SelectedConversationFilter}"
                      DisplayMemberPath="Name" />
            <ui:Button Grid.Column="5" Margin="8,0,0,0" Appearance="Secondary"
                       Content="Conversations…" Command="{Binding ManageConversationsCommand}"
                       ToolTip="Add, rename, or delete conversations" />
        </Grid>
```

- [ ] **Step 2: Add the current-step highlight to the phrase tile style**

Modify `src/AdaVoice.App/Theme/Controls.xaml` — add a trigger before the existing `IsPlaying` one, so a genuinely-playing tile still wins the accent colour if both happen to be true at once:

```xml
    <Style x:Key="PhraseButtonStyle" TargetType="{x:Type ui:Button}">
        <Setter Property="MinWidth" Value="150" />
        <Setter Property="MinHeight" Value="96" />
        <Setter Property="Margin" Value="4" />
        <Setter Property="Padding" Value="12" />
        <Setter Property="HorizontalContentAlignment" Value="Left" />
        <Setter Property="VerticalContentAlignment" Value="Top" />
        <Setter Property="BorderThickness" Value="0" />
        <Style.Triggers>
            <!-- Muted ring on the step the operator is expected to play next (Conversation mode) -->
            <DataTrigger Binding="{Binding IsCurrentStep}" Value="True">
                <Setter Property="BorderThickness" Value="2" />
                <Setter Property="BorderBrush" Value="{StaticResource Text.Secondary}" />
            </DataTrigger>
            <!-- Accent ring while this phrase is playing -->
            <DataTrigger Binding="{Binding IsPlaying}" Value="True">
                <Setter Property="BorderThickness" Value="2" />
                <Setter Property="BorderBrush" Value="{StaticResource Accent}" />
            </DataTrigger>
        </Style.Triggers>
    </Style>
```

- [ ] **Step 3: Add the empty-conversation card**

Modify `src/AdaVoice.App/MainWindow.xaml` — add after the `SearchNoMatch` card (after line 232, before the closing `</Grid></Grid>` at lines 233-234):

```xml
        <!-- A conversation is active and none of its phrases are on the board (distinct from
             CategoryIsEmpty, which this can never overlap with — see BoardViewModel.ConversationIsEmpty). -->
        <ui:Card VerticalAlignment="Center" HorizontalAlignment="Center" Padding="24" MaxWidth="320"
                 Visibility="{Binding ConversationIsEmpty, Converter={StaticResource BoolToVis}}">
            <StackPanel>
                <TextBlock HorizontalAlignment="Center"
                           FontSize="{StaticResource FontSize.SectionTitle}" FontWeight="SemiBold">
                    <Run Text="No phrases in " /><Run Text="{Binding SelectedConversationFilter.Name, Mode=OneWay}" /><Run Text=" yet." />
                </TextBlock>
                <TextBlock Text="Add phrases to it from Conversations…" TextWrapping="Wrap"
                           TextAlignment="Center" Foreground="{StaticResource Text.Secondary}" Margin="0,8,0,0" />
            </StackPanel>
        </ui:Card>
```

- [ ] **Step 4: Manual verification**

Run: `dotnet build` (WPF XAML errors only surface at build time)

Then run the app and:
1. Confirm the Board shows both a "Categories…" and a "Conversations…" button, each opening its own dialog.
2. Create a conversation with 2-3 phrases (Task 6's dialog). Select it from the new dropdown — confirm the Board filters to just those phrases, in the order set in the dialog, and the Category dropdown greys out.
3. Play the first phrase — confirm its tile's highlight ring moves to the next one.
4. Click a later phrase out of order — confirm the highlight moves to just after it (or disappears if it was the last step).
5. Switch back to "All categories" via the Category dropdown — confirm the Conversation dropdown resets to "None" and every phrase reappears.
6. Delete every phrase from a conversation (via the dialog) and select it — confirm the empty-state card appears instead of an empty grid.

- [ ] **Step 5: Full regression pass**

Run: `dotnet test`
Expected: PASS — full suite (Core, Audio, Wasapi, Host, App), no regressions from the XAML-only changes in this task.

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.App/MainWindow.xaml src/AdaVoice.App/Theme/Controls.xaml
git commit -m "feat(app): Board UI for Conversations — selector, step highlight, empty state"
```
