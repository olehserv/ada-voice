# Phrase versions — implementation plan

_Approved 2026-07-07. Combined design + plan (went through Plan Mode, not the
brainstorming skill, so there is no separate spec doc — this file is the single
source of truth for the feature)._

## Revision (2026-07-08)

After shipping, the "Managing versions in the Edit dialog" section below was
**replaced**: versions no longer live inside `PhraseEditDialog`/`PhraseEditViewModel`
at all. Instead there is a dedicated **Versions window** (`PhraseVersionsDialog` /
`PhraseVersionsViewModel`) opened by the context menu's "Versions…" item — a
board-like tile grid (primary tile first, then one tile per version), each
tile playable, a version tile also renamable/deletable inline, plus an "Add
version (record)…" button. `BoardViewModel` gained a `ShowVersionsCommand`
(mirroring `Edit`) instead of routing "Versions…" through `EditCommand`. Every
other section of this plan (data model, playback resolution, recording
pipeline, context menu placement, archive scope, conversation flag) is
unchanged and still accurate — only where version management is *displayed*
moved out of the Edit dialog.

## Context

Operators record one WAV per phrase today. The user wants alternate takes of the
same phrase ("different tones etc") — a **version** — so a Conversation can vary
delivery instead of playing the identical recording every time.

Confirmed behavior (from the user directly):
- A phrase always keeps its **primary** recording (today's single WAV). Versions
  are additional takes on top — never a replacement.
- A normal board click **always** plays the primary. No randomization outside a
  Conversation.
- `Conversation` gets a new boolean flag. When **on**, playing a phrase as a
  Conversation step picks uniformly at random from **primary + all versions**.
  When **off** (default), a Conversation step also just plays the primary —
  identical to today's behavior.
- Versions are shown in the phrase tile's right-click context menu, and are
  recorded/renamed/deleted/tested from the existing Edit dialog — not from the
  context menu directly.

This plan was produced by exploring the current code in depth (Conversation
domain type, the single `BoardViewModel.Play` call site that both board clicks
and Conversation steps share, `PhraseLibraryService`'s edit/delete discipline,
`EngineHost`'s "never destroy a recording" rule, the Edit dialog's structure)
and by drafting and cross-checking a concrete design against that code.

## Data model

**New file** `src/AdaVoice.Core/Domain/PhraseVersion.cs`:
```csharp
public sealed record PhraseVersion
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string FileName { get; init; } = "";
    public int DurationMs { get; init; }
    public double GainDb { get; init; }
    public DateTime CreatedAt { get; init; }
}
```
Each version gets its **own** `GainDb` — different takes have different natural
loudness, and `RecordingResult` already computes a gain per take, so this is free.

**`PhraseEntry`** (`src/AdaVoice.Core/Domain/PhraseEntry.cs`) — add one field:
```csharp
public IReadOnlyList<PhraseVersion> Versions { get; init; } = [];
```

**`Conversation`** (`src/AdaVoice.Core/Domain/Conversation.cs`) — add one field:
```csharp
public bool UseRandomVersion { get; init; }
```

**No `Library.Version` bump needed.** `LibraryJson` (`src/AdaVoice.Core/Storage/LibraryJson.cs`)
is a plain `JsonSerializer.Serialize/Deserialize<Library>` — no source-gen
allow-list. A property absent from an old `library.json` just keeps the C#
default (`[]` / `false`), exactly like `Conversation.PhraseIds` and
`Library.Conversations` already do. Add a round-trip test that loads an
old-format file and asserts `Versions == []`, `UseRandomVersion == false`.

**Id prefix:** use `"pv-"` for version ids. The obvious `"v-"` is already taken
by Conversation ids (`PhraseLibraryService.cs:273`) — reusing it would make ids
ambiguous in logs/exports.

**Version WAV filename:** `"{phraseId}-{versionId}.wav"` (e.g.
`p-1a2b3c4d-pv-9f8e7d6c.wav`) — distinct from the primary's `{id}.wav` and from
`deleted-{id}.wav`, via `AdaVoicePaths.AudioPath(root, fileName)` (no changes
needed there, it's just path-joining).

## Playback: where the random pick happens

**In `BoardViewModel`, not `EngineHost`.** Confirmed there is exactly one
playback call site — `BoardViewModel.Play` (`BoardViewModel.cs:534-571`) — used
for both an ordinary board click and a Conversation step (the only difference
today is that `Play` also calls `AdvanceStepFor` afterward when
`IsConversationActive`). Keeping `EngineHost`/`IPlaybackHost` version-agnostic
(they just play whatever file+gain they're handed) means:

**`IPlaybackHost.PlayEntry`** (`src/AdaVoice.Host/IPlaybackHost.cs:26`) gains an
optional parameter, source-compatible with the existing single-arg call:
```csharp
void PlayEntry(PhraseEntry entry, PhraseVersion? version = null);
```
**`EngineHost.PlayEntry`** (`EngineHost.cs:243-265`): when `version` is
non-null, resolve `version.FileName`/`version.GainDb` instead of the entry's.
The `Phrase` id passed to `Play(new Phrase(entry.Id, samples))` stays the
**entry's** id regardless of which take played — `PlayingPhraseChanged` and
`AdvanceStepFor` (keyed on phrase id, not take) need zero changes.

**`BoardViewModel`** additions:
- A `Random` seam so tests can assert deterministically: constructor param
  `Random? rng = null` → `_rng = rng ?? Random.Shared;`.
- Track the active Conversation's flag alongside the existing
  `_activeConversationPhraseIds` in `OnSelectedConversationFilterChanged`
  (`BoardViewModel.cs:411-451`): add `_activeConversationUseRandomVersion = value.UseRandomVersion;`
  in the `if (!string.IsNullOrEmpty(value?.Id))` branch, and reset it to
  `false` in the `else` branch.
- New helper:
  ```csharp
  private PhraseVersion? PickVersion(PhraseEntry entry)
  {
      if (entry.Versions.Count == 0)
          return null;
      var candidates = new PhraseVersion?[] { null }.Concat(entry.Versions).ToArray();
      return candidates[_rng.Next(candidates.Length)];
  }
  ```
- `Play` (`BoardViewModel.cs:568`) changes to:
  ```csharp
  var version = IsConversationActive && _activeConversationUseRandomVersion
      ? PickVersion(item.Entry) : null;
  _playback.PlayEntry(item.Entry, version);
  ```
  This one line **is** the board-click-vs-Conversation-step distinction — no
  separate code path needed, since `Play` is already the only call site.

## Recording a new version

Reuse the existing recording pipeline (`StartRecording`/`StopRecording`/`SaveTake`
in `BoardViewModel`) — do not build a second state machine.

**New field**, alongside `_pendingMetadata` (`BoardViewModel.cs:322`):
```csharp
private string? _pendingVersionForPhraseId;
```
Do **not** overload `_pendingMetadata` for this — the version path must not
apply category/tags, and seeding `NewTitle` from the phrase's title would be a
confusing default label.

**Every existing site that clears `_pendingMetadata` must also clear
`_pendingVersionForPhraseId`** — this is the main risk in the whole design; a
leaked stash silently files the operator's next unrelated recording as a
"version" of some old phrase. Sites (`BoardViewModel.cs`): `StartRecording`'s
`!ShowRecordButton` branch (~676), not-Live branch (~685), `started == false`
branch (~705), catch block (~715); `StopRecording`'s no-signal branch (~764),
catch block (~775); `DiscardTake` (~861).

**`SaveTake`** (`BoardViewModel.cs:816-855`) branches at the top: if
`_pendingVersionForPhraseId` is set, call a new `IRecorderHost.SaveTakeAsVersion`
instead of `SaveTake`, update the existing `PhraseItemViewModel` in place
(`Phrases.FirstOrDefault(p => p.Entry.Id == entry.Id)?.Update(entry)`), and
**do not** `Phrases.Add(...)` — a version must not create a new tile. On
failure, keep `PendingTake` and the stash so the operator can retry (mirrors
today's error handling).

**Triggering "record a new version" from the Edit dialog:** the dialog exposes
a `RequestedRecordVersion` flag (set by a new "Add version" button, which also
closes the dialog — mirrors the repair dialog's existing "Re-record" choice
closing and handing off to `StartRecording`). `BoardViewModel.Edit`
(`BoardViewModel.cs:602-620`) becomes `async Task` and, after handling
Save/Cancel as today, adds:
```csharp
if (edit.RequestedRecordVersion)
{
    _pendingVersionForPhraseId = item.Entry.Id;
    await StartRecording();
}
```
No `AllowConcurrentExecutions` needed on `Edit` — the dialog already closed
synchronously before `StartRecording` begins.

**New interface members:**
- `IRecorderHost` (`src/AdaVoice.Host/IRecorderHost.cs`):
  `PhraseEntry? SaveTakeAsVersion(RecordingResult result, string phraseId, string label);`
- `ILibraryHost` (`src/AdaVoice.Host/ILibraryHost.cs`):
  `PhraseEntry? DeletePhraseVersion(string phraseId, string versionId);` and
  `PhraseEntry? SetPhraseVersionLabel(string phraseId, string versionId, string label);`
  (`AddPhraseVersion` is **not** on `ILibraryHost` — it needs to write audio,
  so like `SaveTake` it lives only on `IRecorderHost`.)
- `IPlaybackHost`: `string? PreviewVersion(PhraseEntry entry, PhraseVersion version);`
  (mirrors `PreviewEntry`, used by the Edit dialog's "play version" action).

**`PhraseLibraryService`** (`src/AdaVoice.Core/PhraseLibraryService.cs`) — three
new methods following the exact `EditPhrase`/`Add`/`Delete` shape (WAV written
before metadata; never `File.Delete`, only rename-to-`deleted-*`; `EnsureWritable()`
first; `_repository.Save(_library)` after mutating `_library.Phrases[index]`):
`AddPhraseVersion(phraseId, label, durationMs, gainDb, Action<string> writeAudio)`,
`DeletePhraseVersion(phraseId, versionId, Action<string,string> orphanAudio)`,
`SetPhraseVersionLabel(phraseId, versionId, label)`.

**`EngineHost`** implements the above, delegating file I/O the same way
`SaveTake`/`DeleteEntry` already do (`EngineHost.cs:334-348`):
`SaveTakeAsVersion` calls `_library.AddPhraseVersion(...)` with a `writeAudio`
delegate that does `WavFile.Save`; `DeletePhraseVersion` calls
`_library.DeletePhraseVersion(...)` with an `orphanAudio` delegate that does
`File.Move(src, ..., overwrite: true)` to `deleted-{versionFileName}`;
`PreviewVersion` mirrors `PreviewEntry` (`EngineHost.cs:369-376`) reading the
version's file/gain instead of the entry's.

## Managing versions in the Edit dialog

**Every version action commits immediately** (add/rename/delete) — not
deferred to Save like Tags. Reasoning: delete is already an irreversible file
rename the instant it runs (mirrors `BoardViewModel.Delete`'s eager
`DeleteEntry`), and add is unavoidably eager (recording requires closing the
dialog). Making rename eager too keeps one consistent rule instead of
per-action special cases.

**Consequence:** `BoardViewModel.Edit` must re-sync `item` from
`_library.Phrases` even when the dialog is **cancelled** — a version deleted
in-dialog before Cancel would otherwise leave the board's cached
`PhraseItemViewModel.Entry.Versions` stale (and a later random pick could
select an orphaned, now-missing version — harmless since `EngineHost.PlayEntry`
already no-ops on a missing file, but still worth avoiding):
```csharp
if (confirmed && edit.Save() is { } updated)
    item.Update(updated);
else
    item.Update(_library.Phrases.First(p => p.Id == item.Entry.Id));
```

**`PhraseEditViewModel`** (`src/AdaVoice.App/ViewModels/PhraseEditViewModel.cs`)
changes:
- Constructor gains `IPlaybackHost playback` (preview is already an
  `IPlaybackHost` concern, not `ILibraryHost`): `PhraseEditViewModel(ILibraryHost library, IPlaybackHost playback, PhraseEntry entry)`.
- `public ObservableCollection<PhraseVersionRowViewModel> Versions { get; }` —
  built from `entry.Versions` in the constructor, same pattern as `Tags`.
- `public bool RequestedRecordVersion { get; private set; }` and
  `[RelayCommand] private void RecordVersion() => RequestedRecordVersion = true;`
- `[RelayCommand] private async Task PlayVersion(PhraseVersionRowViewModel? row)`
  — off the UI thread like `TestOnHeadphones`: `await Task.Run(() => _playback.PreviewVersion(_entry, row.Version))`.
  There's no toast channel on this dialog today; swallow a failed preview for
  v1 rather than adding new UI plumbing (call out as deferred).
- `[RelayCommand] private void RenameVersion(...)` → `_library.SetPhraseVersionLabel(...)` eagerly.
- `[RelayCommand] private void DeleteVersion(...)` → `_library.DeletePhraseVersion(...)` eagerly, then remove the row.

**`PhraseEditDialog.xaml`** — insert a new "Versions" section between the Tags
`ItemsControl` and the Cancel/Save button row (it's one flat `StackPanel`, no
grid renumbering needed): a label, an `ItemsControl` over `Versions` (each row:
label textbox, ▶ play button, ✕ delete button — same
`PlacementTarget`-free direct-binding style already used elsewhere in this
dialog), and an "Add version (record)…" button bound to `RecordVersionCommand`.
`PhraseEditDialog.xaml.cs` gets a small `AddVersion_Click` handler (mirrors
`Save_Click`): execute `RecordVersionCommand`, then `DialogResult = false` to
close.

## Context menu

`MainWindow.xaml:248-261` — add one `MenuItem` under "Edit…" that reuses the
**same** `EditCommand` (no new command, no new binding plumbing):
```xml
<MenuItem Header="Versions…"
          Command="{Binding PlacementTarget.Tag.EditCommand, RelativeSource={RelativeSource AncestorType=ContextMenu}}"
          CommandParameter="{Binding}" />
```
This satisfies "versions shown in the context menu" as a discoverable entry
point, while all real management stays in the Edit dialog per the confirmed
requirement.

## Conversation flag: the UI toggle

The random-version flag needs a way to be turned on per Conversation. Add it to
the existing "Manage conversations" dialog (`ManageConversationsDialog.xaml`,
`ConversationsViewModel.cs`) next to the row's Name/Save/Delete controls:

- `ILibraryHost`: `Conversation? SetConversationUseRandomVersion(string id, bool useRandomVersion);`
  (mirrors `RenameConversation`'s shape).
- `PhraseLibraryService`: analogous `EditPhrase`-style mutator for `Conversation`
  (mirrors the existing `RenameConversation`/`SetConversationPhrases`).
- `ConversationRowViewModel` (`ConversationsViewModel.cs:81+`): add
  `[ObservableProperty] private bool _useRandomVersion;` seeded from
  `conversation.UseRandomVersion` in the constructor, with
  `partial void OnUseRandomVersionChanged(bool value) => _library.SetConversationUseRandomVersion(Id, value);`
  — eager persist, consistent with "no separate Save step" already documented
  on this class.
- `ManageConversationsDialog.xaml`: add a `CheckBox Content="Play a random version"
  IsChecked="{Binding UseRandomVersion}"` in the row header area (near line 58-66,
  alongside the Name textbox).

## Archive export/import: versions are dropped in v1 (explicitly, not silently)

`LibraryArchiveService.CreateArchive` (`src/AdaVoice.Core/Storage/LibraryArchiveService.cs:186-200`)
already only ever zips `audio/{phrase.FileName}` (the primary) — no code change
needed there for the audio itself. But the embedded `library.json` currently
carries each phrase's full `Versions` list verbatim, which would create
phantom references on import. Fix:
- **Export:** before serializing, strip versions:
  `library with { Phrases = library.Phrases.Select(p => p with { Versions = [] }).ToList() }`.
  Return/log the dropped count so this isn't silent — `EngineHost`'s export
  path logs via its existing `_log` callback when count > 0, e.g. "export:
  dropped N version recording(s) — not included in exports (v1 limitation)."
- **Import:** defensively strip `Versions = []` in the same re-keying block
  that already normalizes `FileName` (`LibraryArchiveService.cs:93-100`), so a
  hand-crafted or third-party archive can't reference version audio that was
  never staged.
- Extending export/import to actually carry version audio is deferred (see
  below) — not needed for correctness, only for completeness.

## Testing

- **`PhraseLibraryServiceTests`**: `AddPhraseVersion` writes audio before
  persisting metadata and returns the updated entry; `DeletePhraseVersion`
  orphans the right file and rejects unknown ids; `SetPhraseVersionLabel`
  touches only the targeted version; `EnsureWritable` guards all three (mirror
  existing writability tests); JSON round-trip proves an old-format file
  defaults `Versions`/`UseRandomVersion` correctly.
- **`LibraryArchiveServiceTests`**: export drops version audio and strips
  `Versions` from the embedded JSON; import strips a crafted archive's
  non-empty `Versions`.
- **`BoardViewModelTests`** (extend the shared `FakePlaybackHost` fake with
  `PlayEntry(entry, version)` recording the passed version, plus
  `PreviewVersion`/`SaveTakeAsVersion`/`DeletePhraseVersion`/`SetPhraseVersionLabel`):
  - Board click (no Conversation active) always plays primary regardless of
    `entry.Versions` — proves the "board always plays primary" requirement.
  - Conversation active with the flag **off** always plays primary — the
    regression test that today's behavior is unchanged by default.
  - Conversation active with the flag **on** and an injected seeded `Random`:
    the exact version picked matches the deterministic sequence — proves the
    pool is primary + all versions, not just versions.
  - Zero-versions edge case: flag on, no versions → always primary.
  - `_pendingVersionForPhraseId` leak coverage: trigger `Edit` →
    `RequestedRecordVersion` → `StartRecording`, hit a no-signal/exception/
    Discard branch, then start an unrelated ordinary recording — assert
    `SaveTake` creates a **new phrase**, not a version. This is the regression
    test for the leak this design explicitly guards against.
  - `SaveTake` version path: after `Edit` sets the stash and a take completes,
    `SaveTake` calls `SaveTakeAsVersion` (not `SaveTake`), updates the existing
    tile, and `Phrases.Count` is unchanged.
  - Edit-cancel resync: version deleted eagerly inside the dialog, dialog
    cancelled → the board's `item.Entry.Versions` still reflects the deletion.

## Explicitly deferred (YAGNI for v1)

- **Reordering versions** — versions have no sequence semantics (unlike
  Conversation steps); add-order is fine.
- **Per-version tags/category** — versions inherit the phrase's; no
  independent metadata per take beyond a label.
- **Weighted random selection** — uniform random is the confirmed requirement.
- **Version audio in archive export/import** — scoped out above; a follow-up
  could add `audio/versions/{fileName}` entries with matching re-keying/staging.
- **In-dialog "Add version" without closing the modal** — would require
  duplicating `BoardViewModel`'s whole recording state machine inside
  `PhraseEditViewModel`; the close-and-reuse-the-existing-pipeline approach is
  strictly simpler and matches the existing repair-dialog precedent.
- **Toast/error surface for a failed in-dialog preview** — swallowed for v1;
  the dialog has no notification channel today.
- **Version count badge on the context menu label or tile** — nice-to-have,
  not required to satisfy "versions shown in the context menu."
- **Undo for eager version rename/delete** — consistent with phrase delete
  itself having no undo.

## Critical files

- `src/AdaVoice.Core/Domain/PhraseEntry.cs`, `Conversation.cs`, new `PhraseVersion.cs`
- `src/AdaVoice.Core/PhraseLibraryService.cs`
- `src/AdaVoice.Core/Storage/LibraryArchiveService.cs`
- `src/AdaVoice.Host/IPlaybackHost.cs`, `IRecorderHost.cs`, `ILibraryHost.cs`, `EngineHost.cs`
- `src/AdaVoice.App/ViewModels/BoardViewModel.cs`, `PhraseEditViewModel.cs`, `ConversationsViewModel.cs`
- `src/AdaVoice.App/PhraseEditDialog.xaml(.cs)`, `MainWindow.xaml`, `ManageConversationsDialog.xaml`
- `tests/AdaVoice.App.Tests/FakePlaybackHost.cs`, `BoardViewModelTests.cs`
- `tests/AdaVoice.Core.Tests/PhraseLibraryServiceTests.cs`, `Storage/LibraryArchiveServiceTests.cs`

## Verification

1. `dotnet build AdaVoice.slnx` — confirm every interface implementer
   (`EngineHost`, `FakePlaybackHost`) compiles against the new signatures.
2. `dotnet test tests/AdaVoice.Core.Tests` — new `PhraseLibraryService`/
   `LibraryArchiveService` tests plus full existing suite green.
3. `dotnet test tests/AdaVoice.App.Tests` — new `BoardViewModel` tests plus
   full existing suite green (this is where the leak-guard and random-pool
   tests live — they're the ones most likely to catch a mistake).
4. `dotnet test AdaVoice.slnx` — full solution, confirm no regressions
   elsewhere (this feature touches four projects' shared interfaces).
5. Manual smoke test: record a phrase, add two versions via Edit, build a
   Conversation with the random flag on, step through it several times and
   confirm different takes audibly play; confirm a plain board click always
   plays the same (primary) take regardless of how many versions exist.
