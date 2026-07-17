# Filter Controls Redesign — Design Spec

_Date: 2026-07-07. Status: approved (brainstorming). Next: implementation plan._

## Problem

Task 7 of the Conversations plan (already implemented, reviewed, and committed on branch
`feat/conversations`) added a Conversation `ComboBox` + "Conversations…" button to the Board's
filter row, next to the pre-existing Category `ComboBox` + "Categories…" button. Real WPF layout
measurement showed the row needs ~550px against ~456px available at the window's default width —
the two `ComboBox`es alone (each `MinWidth="160"`) leave no room for both manage buttons plus the
Record button, which risks being clipped.

Rather than just shrinking the existing controls (a subagent already tried — no `MinWidth`/margin
tuning closes the gap; see the Conversations plan's Task 7 fix history in git log, the plan file
itself was removed once its work shipped), the owner wants a different interaction model: merge each filter's dropdown and its
manage-button into a single button that opens a menu. While doing this, the Category filter also
gains real multi-select (checkboxes) — a small behavior improvement on top of the layout fix,
since the two filters no longer share one UI pattern by coincidence but by design.

**Scope:** this changes the **pre-existing Category filter** (built weeks before the Conversations
feature, in the original Board library UI) as well as the **new Conversation filter** (built in
this plan's Tasks 5-7). The Conversation filter's behavior is explicitly **not** changing — it
stays single-select, because the step-pointer/next-expected-phrase highlight (Task 5) only makes
sense for exactly one active script.

## Architecture

Two small, symmetric UI units replace today's four filter-row elements (2 `ComboBox`es + 2
buttons):

```
Board filter row
├── "Categories" button  → ContextMenu: Manage categories… | separator | ☑ per category
└── "Conversations" button → ContextMenu: Manage conversations… | separator | ○ per conversation
```

- **Categories: real multi-select.** `BoardViewModel` replaces its single `SelectedCategoryFilter`
  with a collection of per-category checkable rows. Filtering becomes a union match (or "show all"
  when nothing is checked).
- **Conversations: unchanged behavior, moved presentation.** `SelectedConversationFilter`,
  `ConversationFilterOptions`, the step pointer, and all of Task 5's mutual-exclusivity logic stay
  exactly as built — only the visual control changes from `ComboBox` to a button + menu.
- **Menu construction is code-behind, not data-bound.** Mixing a fixed "Manage…" action row with a
  dynamically-bound checkable list in one native `ContextMenu` has no clean pure-XAML/MVVM answer
  (no first-class "heterogeneous menu items" binding support in WPF). `MainWindow.xaml.cs` builds
  each menu's `MenuItem`s imperatively, fresh, right before showing it — matching how this codebase
  already keeps WPF-specific glue (dialog construction, the phrase tile's context menu) in
  code-behind rather than forcing it through bindings.

## 1. Category filter: from single-select to multi-select

```csharp
// src/AdaVoice.App/ViewModels/CategoryFilterItemViewModel.cs (new)
public sealed partial class CategoryFilterItemViewModel : ObservableObject
{
    public Category Category { get; }

    [ObservableProperty]
    private bool _isChecked;

    public CategoryFilterItemViewModel(Category category) => Category = category;
}
```

`BoardViewModel` changes:
- `CategoryFilterOptions: IReadOnlyList<Category>` (with the `AllCategories` sentinel) is replaced
  by `CategoryFilterItems: ObservableCollection<CategoryFilterItemViewModel>` — one row per real
  category, rebuilt after `ManageCategories` runs (categories may have been added/renamed/deleted),
  no sentinel needed.
- `SelectedCategoryFilter: Category` is removed. In its place, a private computed set:
  `EffectiveCategoryIds => CategoryFilterItems.Where(i => i.IsChecked).Select(i => i.Category.Id).ToHashSet()`.
- `Matches(entry, search, categoryIds)`: the category check becomes
  `categoryIds.Count > 0 && !categoryIds.Contains(entry.CategoryId) → no match` (empty set matches
  everything).
- `CategoryFilterButtonLabel` (new, string): `"Categories"` when nothing is checked, the single
  category's name when exactly one is checked, `"{N} categories"` when 2+ are checked.
- Each `CategoryFilterItemViewModel.IsChecked` change is observed by `BoardViewModel` (subscribed
  in the constructor, same idea as an event handler on each row) to: re-run the filter, turn off an
  active Conversation on a check (never on an uncheck), and refresh `CategoryFilterButtonLabel`.

## 2. Conversation filter: presentation only

No change to `Conversation`, `SelectedConversationFilter`, `ConversationFilterOptions`,
`IsConversationActive`, the step pointer, or `OnSelectedConversationFilterChanged`'s logic
(Task 5). Two additions:
- `ConversationFilterButtonLabel` (new, string): `"Conversations"` when `SelectedConversationFilter`
  is the `NoneConversation` sentinel, else the active conversation's name.
- `OnSelectedConversationFilterChanged`'s existing "turn off the category filter" branch changes
  from `SelectedCategoryFilter = AllCategories` to clearing every `CategoryFilterItems[i].IsChecked`
  that is currently `true`.

## 3. Mutual exclusivity (unchanged rule, new mechanism)

- Checking a category (`false → true`) while a Conversation is active turns the Conversation off
  (`SelectedConversationFilter = NoneConversation`). Unchecking never does.
- Selecting a Conversation clears every checked category back to unchecked. This does **not**
  re-trigger the "turn off Conversation" branch, because that branch only fires on a check
  transitioning to `true`, and clearing sets each to `false` — no re-entrancy loop, same structural
  argument that made Task 5's original Category↔Conversation exclusivity safe.

## 4. Empty-state CTA with multi-select

Today: selecting a category with zero phrases (and no active search) shows a "Record into
{category}" card. With 2+ categories checked, there's no single target to record into.

**Decision:** the record-into-category CTA (`CategoryIsEmpty`) only appears when **exactly one**
category is checked — identical to today's single-select behavior. With 2+ checked and no matches,
the board falls back to the existing generic "no phrases match" card (currently used for
search-no-match), retitled to not specifically say "search" when the empty state was reached via
categories instead of a search term.

## 5. Menu construction (code-behind)

`MainWindow.xaml.cs` gains two methods, e.g. `ShowCategoryFilterMenu()` /
`ShowConversationFilterMenu()`, each triggered by its button's `Click`. Each method:
1. Builds a `ContextMenu` fresh: a "Manage categories…" (or "Manage conversations…") `MenuItem`
   wired to the existing `ManageCategoriesCommand`/`ManageConversationsCommand`, a `Separator`,
   then one `MenuItem` per row from `board.CategoryFilterItems` (or `ConversationFilterOptions`
   skipping the sentinel).
2. For Categories: each row `MenuItem.IsCheckable = true`, `IsChecked` initialized from the view-model
   row, `Checked`/`Unchecked` handlers write back to `CategoryFilterItemViewModel.IsChecked`.
3. For Conversations: each row's `Click` handler sets `board.SelectedConversationFilter` directly
   to that row's conversation (or to the `NoneConversation` sentinel for a "None" row) — Task 5's
   existing setter already handles single-select activation and the step-pointer reset. The menu
   itself does not track checked-state; each row's `IsChecked` is only set once, at menu-build
   time, from whether that row's conversation is the current `SelectedConversationFilter` — purely
   a visual "you are here" mark, not an interactive toggle.
4. Sets `ContextMenu.PlacementTarget` to the button and opens it (`IsOpen = true`), so it behaves
   like a dropdown anchored under the button rather than a right-click context menu.

## 6. Testing

- **App.Tests:** `CategoryFilterItemViewModel` is a trivial `ObservableObject` — no dedicated test
  file needed beyond what `BoardViewModelTests` already exercises through it.
- **BoardViewModelTests:** replace/extend the existing Category-filter tests for the new multi-select
  shape — checking 0/1/2+ items, union matching, the CTA-only-at-exactly-one-checked rule, and the
  two mutual-exclusivity directions (checking a category clears an active conversation; selecting a
  conversation clears all checked categories). The existing Conversation-filter tests from Task 5
  need no logic changes, only updating any assertion that referenced the removed
  `SelectedCategoryFilter`/`CategoryFilterOptions`/`AllCategories` members.
- **No test for the `ContextMenu` construction itself** — it's WPF glue in code-behind, same
  precedent as the rest of this app's dialog-opening code (`ShowManageCategories`,
  `ShowRepairDialog`, etc.), verified by manual/visual check, not xunit.

## Impact on already-completed plan tasks

This spec supersedes parts of the Conversations plan (removed from `docs/superpowers/plans/`
once shipped — see git history):
- **Task 5** (`BoardViewModel`): the Conversation-side additions (selector, mutual exclusivity,
  step pointer) stand as built. Only the "turn off Category filter" half of the mutual-exclusivity
  code needs updating to the new checkbox-clearing mechanism (§3 above).
- **Task 7** (Board UI): the Conversation `ComboBox` added in the prior commit is replaced by the
  new button+menu pattern; the pre-existing Category `ComboBox` is replaced the same way. The
  tile-highlight and empty-state-card work from Task 7 is unaffected.
- A new implementation plan (this spec's output) will cover exactly these deltas — it does not
  redo Tasks 1-4 or 6, which are untouched by this change.
