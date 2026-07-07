# Filter Controls Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Board's Category `ComboBox`+button and Conversation `ComboBox`+button pairs with two compact buttons, each opening a native menu — fixing a filter-row width overflow — and give Category filtering real multi-select (checkboxes) while Conversation filtering stays single-select.

**Architecture:** `BoardViewModel` gains a `CategoryFilterItems` collection of small per-category checkable view-models, replacing the single `SelectedCategoryFilter`/`CategoryFilterOptions`/`AllCategories` sentinel model. `SelectedConversationFilter` and the step-pointer logic (from the Conversations plan) are untouched — only a button label is added. The two `ComboBox`+button pairs in `MainWindow.xaml` become two `ui:Button`s whose `Click` handlers build a `ContextMenu` in `MainWindow.xaml.cs` (native WPF checkable `MenuItem`s — no data-binding for the heterogeneous "action row + checkable list" shape).

**Tech Stack:** .NET / WPF, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), WPF-UI (`ui:Button`), xUnit.

## Global Constraints

- Zero categories checked = show all phrases (no "All categories" sentinel item). Checking categories narrows the board to the union of their phrases.
- Checking any category while a Conversation is active turns the Conversation off. Selecting a Conversation clears every checked category. Unchecking a category never affects the Conversation.
- Conversation filtering behavior (single-select, step pointer, mutual exclusivity trigger direction) is unchanged from the existing Conversations plan — only its control's visual presentation changes.
- The "record into category" CTA (`CategoryIsEmpty`) only appears when **exactly one** category is checked. 2+ checked with no matches shows a new generic "no phrases match the checked categories" card instead — no CTA (no single target to record into).
- Menu construction is imperative C# in `MainWindow.xaml.cs`, not XAML data-binding — matches this codebase's existing pattern of keeping WPF-specific glue (dialog construction, the phrase tile's context menu) in code-behind.
- No new host/service/domain changes — this plan is entirely `AdaVoice.App` (ViewModels + Views).

---

### Task 1: `BoardViewModel` — Category multi-select + Conversation button label

**Files:**
- Create: `src/AdaVoice.App/ViewModels/CategoryFilterItemViewModel.cs`
- Modify: `src/AdaVoice.App/ViewModels/BoardViewModel.cs`
- Test: `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`

**Interfaces:**
- Produces (consumed by Task 2's `MainWindow.xaml.cs` and Task 3's `MainWindow.xaml`):
  - `CategoryFilterItemViewModel(Category category)`: `Category Category { get; }`, `bool IsChecked { get; set; }`
  - `BoardViewModel.CategoryFilterItems: ObservableCollection<CategoryFilterItemViewModel>`
  - `BoardViewModel.CategoryFilterButtonLabel: string`, `BoardViewModel.ConversationFilterButtonLabel: string`
  - `BoardViewModel.EffectiveSingleCategoryName: string?` (for the CTA card's XAML binding)
  - `BoardViewModel.MultipleCategoriesNoMatch: bool` (for the new generic empty-state card)
  - Removed: `BoardViewModel.SelectedCategoryFilter`, `BoardViewModel.CategoryFilterOptions`, `BoardViewModel.AllCategories`

- [ ] **Step 1: Create `CategoryFilterItemViewModel`**

Create `src/AdaVoice.App/ViewModels/CategoryFilterItemViewModel.cs`:

```csharp
using AdaVoice.Core.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AdaVoice.App.ViewModels;

/// <summary>One checkable row in the Categories filter menu. <see cref="IsChecked"/> is observed by
/// <see cref="BoardViewModel"/> to re-run the filter and enforce mutual exclusivity with the
/// Conversation filter — see BoardViewModel.OnCategoryFilterItemChanged.</summary>
public sealed partial class CategoryFilterItemViewModel : ObservableObject
{
    public Category Category { get; }

    [ObservableProperty]
    private bool _isChecked;

    public CategoryFilterItemViewModel(Category category) => Category = category;
}
```

- [ ] **Step 2: Write the failing tests**

Append these new tests to `tests/AdaVoice.App.Tests/BoardViewModelTests.cs` (inside the `BoardViewModelTests` class — a good spot is right after the existing `Category_is_not_empty_when_all_categories_is_selected` test, near line 923):

```csharp
    [Fact]
    public void Checking_two_categories_shows_phrases_from_either()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }, new Category { Id = "c-2", Name = "Closers" }],
            Phrases =
            [
                new PhraseEntry { Id = "p-1", Title = "A", CategoryId = "c-1" },
                new PhraseEntry { Id = "p-2", Title = "B", CategoryId = "c-2" },
                new PhraseEntry { Id = "p-3", Title = "C", CategoryId = Category.DefaultId },
            ],
        };
        var board = NewBoard(host);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;

        Assert.Equal(["A", "B"], VisibleTitles(board));
    }

    [Fact]
    public void Unchecking_the_last_category_shows_every_phrase_again()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }],
            Phrases =
            [
                new PhraseEntry { Id = "p-1", Title = "A", CategoryId = "c-1" },
                new PhraseEntry { Id = "p-2", Title = "B", CategoryId = Category.DefaultId },
            ],
        };
        var board = NewBoard(host);
        var item = board.CategoryFilterItems.Single(i => i.Category.Id == "c-1");
        item.IsChecked = true;
        Assert.Equal(["A"], VisibleTitles(board));

        item.IsChecked = false;

        Assert.Equal(["A", "B"], VisibleTitles(board));
    }

    [Fact]
    public void Two_categories_checked_with_no_matches_shows_the_generic_empty_state_not_the_cta()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }, new Category { Id = "c-2", Name = "Closers" }],
            Phrases = [new PhraseEntry { Id = "p-1", Title = "Hi", CategoryId = Category.DefaultId }],
        };
        var board = NewBoard(host);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;

        Assert.False(board.CategoryIsEmpty); // 2+ checked — no single target to record into
        Assert.True(board.MultipleCategoriesNoMatch);
    }

    [Fact]
    public void Category_filter_button_label_summarizes_the_checked_set()
    {
        var host = new FakePlaybackHost
        {
            Categories = [new Category { Id = "c-1", Name = "Openers" }, new Category { Id = "c-2", Name = "Closers" }],
        };
        var board = NewBoard(host);
        Assert.Equal("Categories", board.CategoryFilterButtonLabel);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;
        Assert.Equal("Openers", board.CategoryFilterButtonLabel);

        board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;
        Assert.Equal("2 categories", board.CategoryFilterButtonLabel);
    }

    [Fact]
    public void Conversation_filter_button_label_reflects_the_active_conversation()
    {
        var host = new FakePlaybackHost
        {
            Phrases = [new PhraseEntry { Id = "p-1" }],
            Conversations = [new Conversation { Id = "v-1", Name = "Cold call", PhraseIds = ["p-1"] }],
        };
        var board = NewBoard(host);
        Assert.Equal("Conversations", board.ConversationFilterButtonLabel);

        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        Assert.Equal("Cold call", board.ConversationFilterButtonLabel);
    }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/AdaVoice.App.Tests --filter "Checking_two_categories_shows_phrases_from_either|Unchecking_the_last_category_shows_every_phrase_again|Two_categories_checked_with_no_matches_shows_the_generic_empty_state_not_the_cta|Category_filter_button_label_summarizes_the_checked_set|Conversation_filter_button_label_reflects_the_active_conversation"`
Expected: FAIL — `CategoryFilterItems`/`CategoryFilterButtonLabel`/`ConversationFilterButtonLabel`/`MultipleCategoriesNoMatch` don't exist yet (compile error).

- [ ] **Step 4: Replace the field/property declarations in `BoardViewModel`**

Modify `src/AdaVoice.App/ViewModels/BoardViewModel.cs` — replace the existing `_selectedCategoryFilter` field (line 86-88):

```csharp
    /// <summary>Live title/tag search. Empty matches everything.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = "";
```

(that field stays as-is — it's shown only as the anchor immediately above `_selectedCategoryFilter`.) Replace the three lines that follow it:

```csharp
    /// <summary>The category to show, or the "All categories" sentinel for no category filter.</summary>
    [ObservableProperty]
    private Category _selectedCategoryFilter;
```

with nothing — delete these three lines entirely. `CategoryFilterItems` (added in Step 6) replaces this state.

- [ ] **Step 5: Update `SelectedConversationFilter`'s notify list**

Modify the `_selectedConversationFilter` field (line 92-95) — add one more `NotifyPropertyChangedFor`:

```csharp
    /// <summary>The conversation to show, or the "None" sentinel for no conversation filter. Mutually
    /// exclusive with the category filter (design: docs/superpowers/specs/2026-07-06-conversations-design.md §3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConversationActive))]
    [NotifyPropertyChangedFor(nameof(CategoryFilterEnabled))]
    [NotifyPropertyChangedFor(nameof(ConversationFilterButtonLabel))]
    private Conversation _selectedConversationFilter;
```

- [ ] **Step 6: Replace the constructor's Category filter setup**

Modify the constructor — replace this block (line 146-148):

```csharp
        // "All categories" + the real categories drive the filter dropdown; default to All.
        CategoryFilterOptions = [AllCategories, .. library.Categories];
        _selectedCategoryFilter = AllCategories;
```

with:

```csharp
        // One checkable row per category — no "All categories" sentinel; zero checked means "show
        // all" (design: docs/superpowers/specs/2026-07-07-filter-controls-redesign.md §1).
        CategoryFilterItems = new ObservableCollection<CategoryFilterItemViewModel>(
            library.Categories.Select(c => new CategoryFilterItemViewModel(c)));
        foreach (var item in CategoryFilterItems)
            item.PropertyChanged += OnCategoryFilterItemChanged;
```

- [ ] **Step 7: Update the `PhrasesView.Filter` delegate**

Modify the filter delegate (line 156-158):

```csharp
        PhrasesView.Filter = o => o is PhraseItemViewModel p
            && Matches(p.Entry, SearchText, EffectiveCategoryId)
            && (_activeConversationPhraseIdSet is null || _activeConversationPhraseIdSet.Contains(p.Entry.Id));
```

replace with:

```csharp
        PhrasesView.Filter = o => o is PhraseItemViewModel p
            && Matches(p.Entry, SearchText, EffectiveCategoryIds)
            && (_activeConversationPhraseIdSet is null || _activeConversationPhraseIdSet.Contains(p.Entry.Id));
```

- [ ] **Step 8: Update `Phrases.CollectionChanged` to also notify the new property**

Modify the handler (line 160-168):

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

replace with:

```csharp
        Phrases.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasPhrases));
            OnPropertyChanged(nameof(CategoryIsEmpty));
            OnPropertyChanged(nameof(MultipleCategoriesNoMatch));
            OnPropertyChanged(nameof(SearchNoMatch));
            OnPropertyChanged(nameof(HasMatches));
            OnPropertyChanged(nameof(ConversationIsEmpty));
        };
```

- [ ] **Step 9: Remove the `AllCategories` sentinel**

Modify — delete these two lines entirely (line 172-173):

```csharp
    /// <summary>Sentinel "show every category" option for the filter dropdown (blank id = no filter).</summary>
    public static readonly Category AllCategories = new() { Id = "", Name = "All categories" };
```

(the `NoneConversation` sentinel right after it, line 175-176, stays untouched.)

- [ ] **Step 10: Replace `CategoryFilterOptions` with `CategoryFilterItems` and add the button labels**

Modify — replace this property (line 207-209):

```csharp
    /// <summary>"All categories" followed by the real categories — the filter dropdown's items. Rebuilt
    /// after the category manager runs, since categories may have been added/renamed/deleted.</summary>
    public IReadOnlyList<Category> CategoryFilterOptions { get; private set; }
```

with:

```csharp
    /// <summary>One checkable row per category — the Categories filter menu's items. Rebuilt after
    /// the category manager runs, since categories may have been added/renamed/deleted.</summary>
    public ObservableCollection<CategoryFilterItemViewModel> CategoryFilterItems { get; private set; }

    /// <summary>The Categories button's label: "Categories" when nothing is checked, the single
    /// checked category's name when exactly one is, or "{N} categories" for 2+.</summary>
    public string CategoryFilterButtonLabel
    {
        get
        {
            var checkedItems = CategoryFilterItems.Where(i => i.IsChecked).ToList();
            return checkedItems.Count switch
            {
                0 => "Categories",
                1 => checkedItems[0].Category.Name,
                _ => $"{checkedItems.Count} categories",
            };
        }
    }

    /// <summary>The Conversations button's label: "Conversations" when none is active, or the
    /// active conversation's name.</summary>
    public string ConversationFilterButtonLabel => IsConversationActive ? SelectedConversationFilter.Name : "Conversations";
```

- [ ] **Step 11: Update `CategoryIsEmpty`, add `EffectiveSingleCategoryName` and `MultipleCategoriesNoMatch`**

Modify — replace this block (line 251-260):

```csharp
    /// <summary>True when a specific category is selected, no search is active, and that category
    /// has no phrases at all — the CTA card offers to record straight into it. Mutually exclusive
    /// with the search-driven no-match state (Task 3): this one requires blank search text.</summary>
    public bool CategoryIsEmpty => HasPhrases
        && !string.IsNullOrEmpty(EffectiveCategoryId)
        && string.IsNullOrWhiteSpace(SearchText)
        && !Phrases.Any(p => p.Entry.CategoryId == EffectiveCategoryId);

    private string? EffectiveCategoryId =>
        string.IsNullOrEmpty(SelectedCategoryFilter?.Id) ? null : SelectedCategoryFilter.Id;
```

with:

```csharp
    /// <summary>True when exactly one category is checked, no search is active, and that category
    /// has no phrases at all — the CTA card offers to record straight into it. Only makes sense for
    /// exactly one checked category (no single target to record into for 2+) — see
    /// <see cref="MultipleCategoriesNoMatch"/> for that case. Mutually exclusive with the
    /// search-driven no-match state: this one requires blank search text.</summary>
    public bool CategoryIsEmpty => HasPhrases
        && EffectiveSingleCategoryId is { } categoryId
        && string.IsNullOrWhiteSpace(SearchText)
        && !Phrases.Any(p => p.Entry.CategoryId == categoryId);

    /// <summary>2+ categories are checked, no search is active, and nothing on the board matches any
    /// of them — the generic empty-state card (distinct from <see cref="CategoryIsEmpty"/>, which
    /// owns the exactly-one-checked case and offers a "record into" CTA that has no single target
    /// here).</summary>
    public bool MultipleCategoriesNoMatch => HasPhrases
        && EffectiveCategoryIds.Count >= 2
        && string.IsNullOrWhiteSpace(SearchText)
        && !HasMatches;

    /// <summary>The checked category's name, when exactly one is checked — bound by the "record
    /// into" CTA card, which only shows in that same condition.</summary>
    public string? EffectiveSingleCategoryName =>
        EffectiveSingleCategoryId is { } id ? CategoryFilterItems.First(i => i.Category.Id == id).Category.Name : null;

    /// <summary>The checked category, when exactly one is checked — the CTA and "record into" flow
    /// only make sense for a single target. Null when zero or 2+ are checked.</summary>
    private string? EffectiveSingleCategoryId
    {
        get
        {
            var checkedIds = EffectiveCategoryIds;
            return checkedIds.Count == 1 ? checkedIds.Single() : null;
        }
    }

    /// <summary>Every checked category's id — the union filter <see cref="Matches"/> applies. Empty
    /// means no category filtering (show everything).</summary>
    private IReadOnlySet<string> EffectiveCategoryIds =>
        CategoryFilterItems.Where(i => i.IsChecked).Select(i => i.Category.Id).ToHashSet();
```

- [ ] **Step 12: Update `Matches`**

Modify (line 279-291):

```csharp
    /// <summary>A phrase matches when its category passes the filter and the search text appears in its
    /// title or any tag. Pure, so it is unit-testable without WPF.</summary>
    private static bool Matches(PhraseEntry entry, string? search, string? categoryId)
    {
        if (categoryId is not null && entry.CategoryId != categoryId)
            return false;
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var term = search.Trim();
        return entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
```

with:

```csharp
    /// <summary>A phrase matches when its category is in the checked set (or the set is empty — no
    /// filtering) and the search text appears in its title or any tag. Pure, so it is unit-testable
    /// without WPF.</summary>
    private static bool Matches(PhraseEntry entry, string? search, IReadOnlySet<string> categoryIds)
    {
        if (categoryIds.Count > 0 && !categoryIds.Contains(entry.CategoryId))
            return false;
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var term = search.Trim();
        return entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.Tags.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
```

- [ ] **Step 13: Replace `ManageCategories`**

Modify (line 293-304):

```csharp
    /// <summary>Open the category manager; when it closes, rebuild the filter dropdown (categories may
    /// have changed) and reset the filter to "All".</summary>
    [RelayCommand]
    private void ManageCategories()
    {
        _showManageCategories(new CategoriesViewModel(_library));

        CategoryFilterOptions = [AllCategories, .. _library.Categories];
        OnPropertyChanged(nameof(CategoryFilterOptions));
        ApplyColors(); // categories may have been recoloured or deleted
        SelectedCategoryFilter = AllCategories; // also refreshes the filter
    }
```

with:

```csharp
    /// <summary>Open the category manager; when it closes, rebuild the checkable rows (categories may
    /// have changed) — every row starts unchecked, same as picking "All" used to.</summary>
    [RelayCommand]
    private void ManageCategories()
    {
        _showManageCategories(new CategoriesViewModel(_library));

        foreach (var item in CategoryFilterItems)
            item.PropertyChanged -= OnCategoryFilterItemChanged;
        CategoryFilterItems = new ObservableCollection<CategoryFilterItemViewModel>(
            _library.Categories.Select(c => new CategoryFilterItemViewModel(c)));
        foreach (var item in CategoryFilterItems)
            item.PropertyChanged += OnCategoryFilterItemChanged;
        OnPropertyChanged(nameof(CategoryFilterItems));
        OnPropertyChanged(nameof(CategoryFilterButtonLabel));
        ApplyColors(); // categories may have been recoloured or deleted
        RefreshFilter();
    }
```

- [ ] **Step 14: Replace `OnSelectedCategoryFilterChanged` with `OnCategoryFilterItemChanged`, update the Conversation-side clearing logic, and update `RefreshFilter`**

Modify — replace this whole block (line 335-395, from `OnSearchTextChanged` through the end of `RefreshFilter`; `OnSearchTextChanged` itself and `OnSelectedConversationFilterChanged`'s inner body are shown for exact anchoring — only `OnSelectedCategoryFilterChanged` is deleted, the category-clearing lines inside `OnSelectedConversationFilterChanged` are replaced, and `RefreshFilter` gains two lines):

```csharp
    partial void OnSearchTextChanged(string value) => RefreshFilter();

    partial void OnSelectedConversationFilterChanged(Conversation value)
    {
        if (!string.IsNullOrEmpty(value?.Id))
        {
            // Populate the active-conversation state before clearing checked categories below —
            // that clear re-enters this class via OnCategoryFilterItemChanged -> RefreshFilter,
            // which raises PropertyChanged(ConversationIsEmpty) while SelectedConversationFilter
            // already reports IsConversationActive == true. If _activeConversationPhraseIdSet were
            // still null at that point, ConversationIsEmpty's `_activeConversationPhraseIdSet!.Contains(...)`
            // would null-ref the moment a live binding reads it. Setting these first keeps the
            // invariant (IsConversationActive == true implies the set is populated) intact throughout.
            _activeConversationPhraseIds = value.PhraseIds.ToList();
            _activeConversationPhraseIdSet = _activeConversationPhraseIds.ToHashSet();
            _currentStepIndex = 0;

            // Activating a Conversation clears every checked category — mutually exclusive. Never
            // re-triggers OnCategoryFilterItemChanged's "turn off the conversation" branch, since
            // that only fires on a check transitioning to true, and this only sets false.
            foreach (var item in CategoryFilterItems.Where(i => i.IsChecked).ToList())
                item.IsChecked = false;

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

    /// <summary>Checking a category while a Conversation is active turns the Conversation off —
    /// mutually exclusive. Unchecking never does. Every check/uncheck re-runs the filter and
    /// refreshes the button label.</summary>
    private void OnCategoryFilterItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CategoryFilterItemViewModel.IsChecked))
            return;

        var item = (CategoryFilterItemViewModel)sender!;
        if (item.IsChecked && IsConversationActive)
            SelectedConversationFilter = NoneConversation;

        RefreshFilter();
        OnPropertyChanged(nameof(CategoryFilterButtonLabel));
    }

    private void RefreshFilter()
    {
        PhrasesView.Refresh();
        OnPropertyChanged(nameof(CategoryIsEmpty));
        OnPropertyChanged(nameof(EffectiveSingleCategoryName));
        OnPropertyChanged(nameof(MultipleCategoriesNoMatch));
        OnPropertyChanged(nameof(SearchNoMatch));
        OnPropertyChanged(nameof(HasMatches));
        OnPropertyChanged(nameof(ConversationIsEmpty));
    }
```

- [ ] **Step 15: Update `RecordIntoCategory`**

Modify (line 628-636):

```csharp
    /// <summary>Record straight into the currently selected (empty) category — the category-empty
    /// CTA's button. Reuses StartRecording exactly as clicking the normal Record button would;
    /// only the pending-category stash differs.</summary>
    [RelayCommand]
    private async Task RecordIntoCategory()
    {
        _pendingMetadata = (_pendingMetadata.Title, EffectiveCategoryId, _pendingMetadata.Tags);
        await StartRecording();
    }
```

with:

```csharp
    /// <summary>Record straight into the currently selected (empty) category — the category-empty
    /// CTA's button. Reuses StartRecording exactly as clicking the normal Record button would;
    /// only the pending-category stash differs. Only reachable when CategoryIsEmpty is true (exactly
    /// one category checked), so EffectiveSingleCategoryId is always non-null here in practice.</summary>
    [RelayCommand]
    private async Task RecordIntoCategory()
    {
        _pendingMetadata = (_pendingMetadata.Title, EffectiveSingleCategoryId, _pendingMetadata.Tags);
        await StartRecording();
    }
```

- [ ] **Step 16: Update every existing test call site**

The removal of `SelectedCategoryFilter`/`CategoryFilterOptions`/`AllCategories` turns every remaining reference into a compile error — `dotnet build` after Step 15 gives you the complete list. Apply this exact substitution rule at each site (all in `tests/AdaVoice.App.Tests/BoardViewModelTests.cs`):

**Rule A** — replace `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "ID");` (or `.First(...)`) with `board.CategoryFilterItems.Single(i => i.Category.Id == "ID").IsChecked = true;`, at these lines (by their current content, since line numbers shift as you edit — search for each exact string):

- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");` in `Save_take_with_a_pending_category_that_fails_to_apply_still_completes_the_save` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.First(c => c.Id == "c-1");` in `Editing_a_phrase_out_of_the_active_filter_hides_it_from_the_view` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.First(c => c.Id == "c-1");` in `Category_filter_limits_to_the_chosen_category` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");` in `Category_is_empty_when_selected_category_has_no_phrases_and_search_is_blank` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-1");` in `Category_is_not_empty_when_it_has_a_phrase` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");` in `Category_is_not_reported_empty_while_search_text_is_active` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");` in `Record_into_category_starts_recording_like_the_normal_Record_button` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");` in `Record_into_category_applies_the_category_to_the_saved_take` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");` in `Discarding_a_take_clears_any_pending_category` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-2");` in `Failed_record_into_category_does_not_leak_pending_metadata_into_the_next_save` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-2").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-1");` in `Search_no_match_is_true_even_with_a_category_selected` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;`
- `board.SelectedCategoryFilter = board.CategoryFilterOptions.Single(c => c.Id == "c-1");` in `Selecting_a_conversation_while_a_category_is_active_does_not_null_ref_a_live_binding` → `board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;`

**Rule B** — the comment on `Category_is_not_empty_when_all_categories_is_selected`'s assertion (`// default filter is "All categories"`) is now inaccurate. Update just the comment, not the assertion (it's still correct: `Assert.False(board.CategoryIsEmpty);`):

```csharp
        Assert.False(board.CategoryIsEmpty); // default: nothing checked, shows every phrase
```

**Rule C** — `Manage_categories_opens_the_manager_then_rebuilds_the_filter_options`. Replace:

```csharp
        // "All categories" sentinel + Uncategorized + the new one.
        Assert.Contains(board.CategoryFilterOptions, c => c.Name == "Greetings");
        Assert.Same(BoardViewModel.AllCategories, board.SelectedCategoryFilter); // reset to All
```

with:

```csharp
        Assert.Contains(board.CategoryFilterItems, i => i.Category.Name == "Greetings");
        Assert.All(board.CategoryFilterItems, i => Assert.False(i.IsChecked)); // every row starts unchecked
```

**Rule D** — `Selecting_a_conversation_turns_off_the_category_filter`. Replace:

```csharp
        board.SelectedCategoryFilter = new Category { Id = "c-1", Name = "Greetings" };

        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        Assert.Equal(BoardViewModel.AllCategories.Id, board.SelectedCategoryFilter.Id);
        Assert.False(board.CategoryFilterEnabled);
```

with:

```csharp
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;

        board.SelectedConversationFilter = board.ConversationFilterOptions.Single(c => c.Id == "v-1");

        Assert.All(board.CategoryFilterItems, i => Assert.False(i.IsChecked)); // cleared
        Assert.False(board.CategoryFilterEnabled);
```

**Rule E** — `Selecting_a_specific_category_turns_off_an_active_conversation`. Replace:

```csharp
        board.SelectedCategoryFilter = host.Categories[0];
```

with:

```csharp
        board.CategoryFilterItems.Single(i => i.Category.Id == "c-1").IsChecked = true;
```

- [ ] **Step 17: Run the tests to verify they pass**

Run: `dotnet test tests/AdaVoice.App.Tests --filter BoardViewModelTests`
Expected: PASS (every Board test, including the 5 new ones from Step 2 and all migrated ones from Step 16)

- [ ] **Step 18: Run the full test suite**

Run: `dotnet test`
Expected: PASS — no regressions in Core, Audio, Wasapi, Host, or the rest of App.

- [ ] **Step 19: Commit**

```bash
git add src/AdaVoice.App/ViewModels/CategoryFilterItemViewModel.cs src/AdaVoice.App/ViewModels/BoardViewModel.cs tests/AdaVoice.App.Tests/BoardViewModelTests.cs
git commit -m "feat(app): Category filter becomes multi-select; add filter-button labels"
```

---

### Task 2: `MainWindow.xaml.cs` — filter menu construction

**Files:**
- Modify: `src/AdaVoice.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `BoardViewModel.CategoryFilterItems`/`ManageCategoriesCommand`/`ConversationFilterOptions`/`SelectedConversationFilter`/`ManageConversationsCommand` (Task 1, and the pre-existing Conversations plan).
- Produces (consumed by Task 3's XAML `Click` bindings): `MainWindow.ShowCategoryFilterMenu(object, RoutedEventArgs)`, `MainWindow.ShowConversationFilterMenu(object, RoutedEventArgs)`.

This is WPF code-behind — no automated test (matches this file's existing untested methods like `ShowManageCategories`). Verified by Task 3's manual check and by `dotnet build`.

- [ ] **Step 1: Add the two menu-building methods**

Modify `src/AdaVoice.App/MainWindow.xaml.cs` — insert directly below the existing `ShowManageConversations` method (near line 119-120; do not duplicate that method, just add these after it):

```csharp
    /// <summary>Open the Categories filter menu: "Manage categories…", then one checkable row per
    /// category. Built fresh on every click (cheap for a handful of rows) so it never shows stale
    /// state. Native ContextMenu + checkable MenuItems, not data-bound — WPF has no clean way to mix
    /// a fixed action row with a dynamically-bound checkable list in one ItemsSource.</summary>
    private void ShowCategoryFilterMenu(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BoardViewModel board || sender is not FrameworkElement button)
            return;

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = button };
        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = "Manage categories…",
            Command = board.ManageCategoriesCommand,
        });
        menu.Items.Add(new System.Windows.Controls.Separator());

        foreach (var item in board.CategoryFilterItems)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = item.Category.Name,
                IsCheckable = true,
                IsChecked = item.IsChecked,
            };
            menuItem.Checked += (_, _) => item.IsChecked = true;
            menuItem.Unchecked += (_, _) => item.IsChecked = false;
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }

    /// <summary>Open the Conversations filter menu: "Manage conversations…", then one row per
    /// conversation (including the "None" sentinel, rendered like any other row). Clicking a row
    /// activates it directly through BoardViewModel.SelectedConversationFilter's existing setter —
    /// the menu does not track checked-state itself, it only reflects the current selection when
    /// built.</summary>
    private void ShowConversationFilterMenu(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BoardViewModel board || sender is not FrameworkElement button)
            return;

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = button };
        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = "Manage conversations…",
            Command = board.ManageConversationsCommand,
        });
        menu.Items.Add(new System.Windows.Controls.Separator());

        foreach (var conversation in board.ConversationFilterOptions)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = conversation.Name,
                IsCheckable = true,
                IsChecked = board.SelectedConversationFilter.Id == conversation.Id,
            };
            menuItem.Click += (_, _) => board.SelectedConversationFilter = conversation;
            menu.Items.Add(menuItem);
        }

        menu.IsOpen = true;
    }
```

- [ ] **Step 2: Run the build**

Run: `dotnet build`
Expected: SUCCEEDED, 0 warnings, 0 errors.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test`
Expected: PASS — this task adds new code-behind methods only; nothing existing calls them yet (Task 3 wires the XAML), so no test should be affected.

- [ ] **Step 4: Commit**

```bash
git add src/AdaVoice.App/MainWindow.xaml.cs
git commit -m "feat(app): build the Categories/Conversations filter menus in code-behind"
```

---

### Task 3: `MainWindow.xaml` — filter row buttons + new empty-state card

**Files:**
- Modify: `src/AdaVoice.App/MainWindow.xaml`

**Interfaces:**
- Consumes: `BoardViewModel.CategoryFilterButtonLabel`/`ConversationFilterButtonLabel`/`CategoryFilterEnabled`/`MultipleCategoriesNoMatch`/`EffectiveSingleCategoryName` (Task 1), `MainWindow.ShowCategoryFilterMenu`/`ShowConversationFilterMenu` (Task 2).

XAML-only — no automated test. Verified by `dotnet build` (Step 2) and manual check (Step 3).

- [ ] **Step 1: Replace the filter row**

Modify `src/AdaVoice.App/MainWindow.xaml` — replace the comment above the search/filter `StackPanel` (currently inaccurate — it claims the row already survives 420px and has room for a Conversations selector "later," which this plan's own predecessor proved false) and the filter-row `Grid` inside it:

Replace this comment (lines 172-173):

```xml
        <!-- Search on its own line, filters below it — both rows survive the 420 px docked
             width, and the filter row has room for the Conversations selector later. -->
```

with:

```xml
        <!-- Search on its own line, filters below it as two compact menu buttons (not dropdowns) —
             keeps the row narrow enough for the docked width (design:
             docs/superpowers/specs/2026-07-07-filter-controls-redesign.md). -->
```

Replace the filter-row `Grid` (lines 191-237, from `<Grid Margin="0,8,0,0">` through its closing `</Grid>`, just before the search `StackPanel`'s closing tag):

```xml
            <Grid Margin="0,8,0,0">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="Auto" />
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <ui:Button Grid.Column="0" Content="{Binding CategoryFilterButtonLabel}"
                           IsEnabled="{Binding CategoryFilterEnabled}"
                           Click="ShowCategoryFilterMenu"
                           ToolTip="Filter by category, or manage categories"
                           AutomationProperties.Name="Category filter" />
                <ui:Button Grid.Column="1" Margin="8,0,0,0" Content="{Binding ConversationFilterButtonLabel}"
                           Click="ShowConversationFilterMenu"
                           ToolTip="Filter by conversation, or manage conversations"
                           AutomationProperties.Name="Conversation filter" />
                <!-- Starts a take immediately and opens the recorder window (BoardViewModel calls
                     back into MainWindow.ShowRecorder). With a take already waiting, it reopens
                     the recorder instead — the lit Caution state says something is unfinished. -->
                <ui:Button Grid.Column="3" Icon="{ui:SymbolIcon Mic24}"
                           Content="Record" Command="{Binding StartRecordingCommand}">
                    <ui:Button.Style>
                        <Style TargetType="ui:Button" BasedOn="{StaticResource {x:Type ui:Button}}">
                            <Setter Property="Appearance" Value="Secondary" />
                            <Setter Property="ToolTip" Value="Record a new phrase (pauses the call feed while recording)" />
                            <Style.Triggers>
                                <DataTrigger Binding="{Binding HasPendingTake}" Value="True">
                                    <Setter Property="Appearance" Value="Caution" />
                                    <Setter Property="ToolTip" Value="A recording is waiting to be saved — click to open it." />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </ui:Button.Style>
                </ui:Button>
            </Grid>
```

- [ ] **Step 2: Add the "2+ categories, no match" empty-state card**

Modify — insert directly below the existing `CategoryIsEmpty` card (after its closing `</ui:Card>`, before the `SearchNoMatch` card; the `CategoryIsEmpty` card's `Run` bindings also change from `SelectedCategoryFilter.Name` to `EffectiveSingleCategoryName`):

Replace the `CategoryIsEmpty` card (currently around line 368-383):

```xml
        <!-- A specific category is selected and it genuinely has no phrases (search is blank) —
             distinct from the search-driven no-match card below. Remember: any new Run.Text bound
             to Category.Name needs Mode=OneWay (Name is init-only — see Global Constraints). -->
        <ui:Card VerticalAlignment="Center" HorizontalAlignment="Center" Padding="24" MaxWidth="320"
                 Visibility="{Binding CategoryIsEmpty, Converter={StaticResource BoolToVis}}">
            <StackPanel>
                <TextBlock HorizontalAlignment="Center"
                           FontSize="{StaticResource FontSize.SectionTitle}" FontWeight="SemiBold">
                    <Run Text="No phrases in " /><Run Text="{Binding SelectedCategoryFilter.Name, Mode=OneWay}" /><Run Text=" yet." />
                </TextBlock>
                <ui:Button Appearance="Primary" HorizontalAlignment="Center" Margin="0,12,0,0"
                           Command="{Binding RecordIntoCategoryCommand}">
                    <TextBlock><Run Text="Record into " /><Run Text="{Binding SelectedCategoryFilter.Name, Mode=OneWay}" /></TextBlock>
                </ui:Button>
            </StackPanel>
        </ui:Card>
```

with:

```xml
        <!-- Exactly one category is checked and it genuinely has no phrases (search is blank) —
             distinct from the search-driven no-match card below and from the 2+-checked generic
             card after it. EffectiveSingleCategoryName is only non-null in this exact condition. -->
        <ui:Card VerticalAlignment="Center" HorizontalAlignment="Center" Padding="24" MaxWidth="320"
                 Visibility="{Binding CategoryIsEmpty, Converter={StaticResource BoolToVis}}">
            <StackPanel>
                <TextBlock HorizontalAlignment="Center"
                           FontSize="{StaticResource FontSize.SectionTitle}" FontWeight="SemiBold">
                    <Run Text="No phrases in " /><Run Text="{Binding EffectiveSingleCategoryName, Mode=OneWay}" /><Run Text=" yet." />
                </TextBlock>
                <ui:Button Appearance="Primary" HorizontalAlignment="Center" Margin="0,12,0,0"
                           Command="{Binding RecordIntoCategoryCommand}">
                    <TextBlock><Run Text="Record into " /><Run Text="{Binding EffectiveSingleCategoryName, Mode=OneWay}" /></TextBlock>
                </ui:Button>
            </StackPanel>
        </ui:Card>

        <!-- 2+ categories are checked and none of them has a matching phrase — no single target to
             record into, so this is a plain message, not a CTA (distinct from CategoryIsEmpty above). -->
        <ui:Card VerticalAlignment="Center" HorizontalAlignment="Center" Padding="24" MaxWidth="320"
                 Visibility="{Binding MultipleCategoriesNoMatch, Converter={StaticResource BoolToVis}}">
            <StackPanel>
                <TextBlock Text="No phrases match the checked categories" HorizontalAlignment="Center"
                           FontSize="{StaticResource FontSize.SectionTitle}" FontWeight="SemiBold"
                           TextWrapping="Wrap" TextAlignment="Center" />
            </StackPanel>
        </ui:Card>
```

- [ ] **Step 3: Run the build**

Run: `dotnet build`
Expected: SUCCEEDED, 0 warnings, 0 errors (WPF XAML errors only surface at build time).

- [ ] **Step 4: Manual verification**

Run the app and:
1. Confirm the filter row now shows two buttons ("Categories" / "Conversations") plus "Record", all visible without clipping at both the default (480px) and minimum (420px) window width.
2. Click "Categories" — confirm the menu shows "Manage categories…", a separator, then one checkable row per category. Check two — confirm the board shows the union of their phrases and the button now reads "2 categories".
3. With 2 categories checked and no matches, confirm the new generic card appears (not the "Record into…" CTA).
4. Uncheck back to zero — confirm every phrase reappears and the button reads "Categories" again.
5. Click "Conversations" — confirm the menu shows "Manage conversations…", a separator, "None", then one row per conversation. Pick one — confirm the checked categories clear, the button shows the conversation's name, and the step highlight still works (play a phrase, watch the highlight move).
6. Check a category while a conversation is active — confirm the conversation turns off (button reverts to "Conversations") and the category filter takes over.

- [ ] **Step 5: Full regression pass**

Run: `dotnet test`
Expected: PASS — full suite, no regressions from the XAML-only changes in this task.

- [ ] **Step 6: Commit**

```bash
git add src/AdaVoice.App/MainWindow.xaml
git commit -m "feat(app): filter row uses two menu buttons instead of dropdown+button pairs"
```
