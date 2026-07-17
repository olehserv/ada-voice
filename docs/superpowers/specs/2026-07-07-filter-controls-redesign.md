# Filter Controls Redesign — Design Spec (decision record)

_Date: 2026-07-07. Status: ✅ Shipped and user smoke-tested. Compacted to the decisions that
outlive the implementation; the full spec is in git history. Supersedes the filter-row UI in
[2026-07-06-conversations-design.md](2026-07-06-conversations-design.md)._

## What changed and why

The Board's filter row (Category `ComboBox` + Conversation `ComboBox` + two manage buttons +
Record) needed ~550 px against ~456 px available — no `MinWidth`/margin tuning closed the gap.
Instead of shrinking controls, each filter's dropdown and its manage button merged into a
single **menu button**:

```
Board filter row
├── "Categories" button    → menu: Manage categories… | separator | ☑ per category
└── "Conversations" button → menu: Manage conversations… | separator | ○ per conversation
```

## Decisions that still matter

- **Categories became real multi-select** (checkable rows, union match; empty selection =
  show all). Not a coincidence of the layout fix — the owner wanted the behavior upgrade.
- **Conversations stayed single-select on purpose:** the step-pointer / next-expected-phrase
  highlight only makes sense for exactly one active script.
- **Menu construction is code-behind, not data-bound.** Mixing a fixed "Manage…" row with a
  dynamically-bound checkable list in one native `ContextMenu` has no clean pure-XAML/MVVM
  answer (no first-class heterogeneous-menu-items binding in WPF). `MainWindow.xaml.cs`
  builds each menu fresh before showing it — matching how this codebase already keeps WPF
  glue (dialog construction, tile context menu) in code-behind. No xunit test for the menu
  glue itself; ViewModel logic is covered in `BoardViewModelTests`.
- **Mutual exclusivity mechanics:** checking a category (false→true) turns an active
  conversation off; unchecking never does. Selecting a conversation clears all checked
  categories — safe from re-entrancy because the turn-off branch only fires on a check
  transitioning to true.
- **Empty-state CTA rule:** the "Record into {category}" card appears only when **exactly
  one** category is checked; with 2+ checked and no matches, the generic "no phrases match"
  card shows instead.
