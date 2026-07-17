# UX / Layout / Style Audit — 2026-07-12

**Status:** live pass-by-pass status is tracked in one place — the status table in
[the plan](../plans/ux-structural-fix-plan.md). This audit records the findings as of
2026-07-12 and is not updated per pass. (F1 was pulled forward per owner feedback and folded
into the plan's `Pass 6`; a separate owner-feedback rework batch — not audit findings — is
described in the plan's "Owner UX rework batch" section, with the resulting conventions in
[wpf-ux-design-rules.md](../wpf-ux-design-rules.md) rule 5.)

Scope: `src/AdaVoice.App` (9 windows/dialogs) against the problems named in the modernization
request, plus a fresh look at layout, buttons, and visual consistency.

**Read together with, not instead of:**
- [09-design-system.md](../09-design-system.md) — tokens, theme, spacing, typography (canonical).
- [05-ui-design.md](../05-ui-design.md) — per-screen specs, window sizing rules.
- [10-ui-redesign-brief.md](../10-ui-redesign-brief.md) — the "Studio Graphite" redesign brief.
- [../../plans/ui-ux-localization-scope.md](../../plans/ui-ux-localization-scope.md) — the
  existing current-state audit and slice plan for the *feature* gaps (responsive layout,
  localization). This audit does not repeat that table; it covers layout/UX mechanics instead.

This audit does **not** re-derive tokens, colors, or type ramp — those already have one source
of truth (09) and are followed consistently (see Finding E1).

## Executive summary

The app went through a professional Fluent redesign yesterday (2026-07-11, "Studio Graphite" —
see [handoff.md](../../../handoff.md)): WPF-UI 4.3 throughout, a token system with zero
hardcoded colors/font sizes in view XAML, dark+light theming with verified contrast. Several
problems named in the modernization brief are **already fixed** and were re-verified here:

- **"Modal windows can be resized by mouse and break layout"** — not currently true. All 8
  dialogs already use `ResizeMode="NoResize"`. The only resizable window is `MainWindow`
  (Finding C1 covers its resize behavior specifically).
- **"Buttons move strangely when windows are resized"** — not reproducible in any dialog
  (all fixed-size or `NoResize`+`SizeToContent`). `MainWindow` layout is deliberately stable at
  its enforced minimum (420×560); see C1 for the one real gap (behavior *above* the minimum is
  undefined/untested).

Real, current issues found:

- **B1 (High) — ✅ FIXED (Pass 2).** `SettingsWindow`'s "Done" button sat *inside* the
  scrollable content — the exact "Save hidden below scroll" bug named in the brief. It was
  **isolated to this one window** — every other dialog with a `ScrollViewer` already kept its
  footer button outside it. Fixed by moving Done to a footer row outside the `ScrollViewer`;
  see [settings-window-review.md](../screenshots/review/settings-window-review.md).
- **E2 (High) — still open (`Pass 2b`).** Every confirm/error/info prompt in normal app flow
  uses the raw Win32 `MessageBox` (6 call sites in `MainWindow.xaml.cs`), which renders with OS
  chrome — a jarring break from the FluentWindow look everywhere else. This is the single
  biggest "looks like an old enterprise tool" tell left in the app.
- **D1 (Medium) — ✅ FIXED (Pass 4).** Destructive actions (Delete category, Delete
  conversation, Remove phrase, Delete version) all used `Appearance="Secondary"` — visually
  identical to non-destructive actions. Now `Appearance="Danger"`; see
  [button-consistency-review.md](../screenshots/review/button-consistency-review.md).
- **C1 (Medium) — ✅ verified, no fix needed (`Pass 3`).** `MainWindow` has `MinWidth`/
  `MinHeight` but no `MaxWidth`/`MaxHeight`, and ships one layout at every width above the
  420 px minimum (the Full/Docked responsive layout is a known, already-tracked open item — see
  [ui-ux-localization-scope.md](../../plans/ui-ux-localization-scope.md)). A permanent
  regression screenshot (`MainWindow_board_wide`, 1366×780) confirmed no wrap/clip/overlap at
  desktop width; no code change needed.
- **F1 (Low) — open, promoted (`Pass 6`, owner request 2026-07-12).** Originally: phrase tile
  titles wrap without a true 2-line clamp, so unusually long titles can make one tile taller
  than its row-mates in the `WrapPanel`. Was deferred to a future polish pass; the owner has now
  asked for it directly, and expanded the scope to also fix tile height/width varying with **tag
  count** (a tile with tags is taller than one without) and add a "+N" overflow indicator when a
  phrase has more tags than the tile can show. See `Pass 6` in the plan — not started.

Everything else audited (button order, `IsDefault`/`IsCancel`, spacing, focus, keyboard) is
already in good shape — see the full findings below for what was checked and passed.

## Findings

| ID | Area | Severity | Status | File(s) | Fix pattern | Risk |
|----|------|----------|--------|---------|-------------|------|
| B1 | Dialog footer buttons | **High** | ✅ Fixed (Pass 2) | `SettingsWindow.xaml` | Move Grid to header/scroll-content/footer 3-row structure; "Done" outside `ScrollViewer` | Low — pure layout move, no logic touched |
| E2 | Visual consistency | **High** | Open (`Pass 2b`) | `MainWindow.xaml.cs` (6 sites), `App.xaml.cs` (2 sites) | Replace in-flow `MessageBox.Show` with `ui:ContentDialog` via `IContentDialogService`; **keep** the 2 in `App.xaml.cs` (single-instance notice before any window exists, global crash handler) as `MessageBox` — a `ContentDialog` needs a live `ContentPresenter`/theme host that may not be reliable during a crash | Medium — touches call sites returning `bool`/tuples; must stay `async`, not `async void`, per dotnet-wpf-design skill CTRL-008 |
| D1 | Button semantics | Medium | ✅ Fixed (Pass 4) | `ManageCategoriesDialog.xaml`, `ManageConversationsDialog.xaml`, `PhraseVersionsDialog.xaml` | `Appearance="Danger"` on Delete/Remove buttons (CTRL-002 in dotnet-wpf-design skill) | Low — `Appearance` is a single attribute swap |
| C1 | MainWindow resize stability | Medium | ✅ Verified, no fix needed (`Pass 3`) | `MainWindow.xaml` | Added a permanent regression screenshot (`MainWindow_board_wide`, 1366×780, 10 phrases) instead of a manual resize; confirmed no clipping/overlap between 420 px and typical desktop widths | None — no code changed |
| F1 | Tile height consistency | Low | Open, promoted (`Pass 6`, owner request 2026-07-12) | `MainWindow.xaml` (phrase tile `DataTemplate`), possibly `PhraseItemViewModel.cs`/`BoardViewModel.cs` | Fixed tile `Width`/`Height` regardless of tag count; `TextTrimming="CharacterEllipsis"` clamp on the title; capped visible tags + a "+N" overflow chip (full list still reachable via the tile's existing "Edit…" context-menu item) | Low-medium — mostly visual, at most one small contained ViewModel addition |
| D2 | `IsDefault` consistency | Low | ✅ Fixed (Pass 4) | `RecorderDialog.xaml` | The "Save" button in the pending-take state has no `IsDefault="True"` (unlike `PhraseEditDialog`/`RepairPhraseDialog`, which do); each state's button is mutually exclusive via `Visibility`, so this is safe to add | Low |

### What was checked and already passes (no action needed)

- **Window behavior:** every dialog sets `WindowStartupLocation="CenterOwner"`,
  `ShowInTaskbar="False"`, and an explicit `ResizeMode`. `MainWindow` enforces
  `MinWidth="420" MinHeight="560"`.
- **Button order/placement:** every dialog with Cancel+Primary puts Cancel left, primary action
  right (`PhraseEditDialog`, `RepairPhraseDialog`) — standard Windows convention, consistent
  everywhere it applies.
- **`IsCancel`:** present on every dialog's dismiss button. `Esc` reliably closes every dialog.
- **Colors/typography:** zero hardcoded hex colors or literal `FontSize` values in view XAML
  outside `Theme/*.xaml` (grep-verified). Type ramp and spacing tokens are used consistently.
- **Focus/keyboard:** `Ctrl+F` focuses search, `Esc` = panic stop at the window level, dialogs
  have working `IsCancel`. WPF-UI supplies standard Tab focus visuals.
- **AutomationProperties:** present on the icon-only buttons in `MainWindow` (Setup, Settings,
  Clear search) and the state dot.

## Priority order for fixing

1. ✅ **B1** — Settings footer fix (isolated, high user-visible impact, lowest risk). Done.
2. **E2** — `ContentDialog` migration for in-flow prompts (highest "professional feel" payoff).
   Still open — next up whenever approved (`Pass 2b`).
3. ✅ **D1** — Danger appearance on destructive buttons (trivial, high clarity payoff). Done.
4. ✅ **D2** — `IsDefault` on Recorder's Save. Done.
5. ✅ **C1** — MainWindow resize verification. Verified, no fix needed (`Pass 3`).
6. **F1** — tile fixed size + tag overflow indicator, scope expanded per owner request
   2026-07-12. Still open (`Pass 6`), promoted ahead of Pass 2b.

## Windows/screens in priority order for modernization passes

1. ✅ `SettingsWindow` (B1) — done.
2. `MainWindow` (E2 still open; C1 verified/closed; F1 open, promoted to `Pass 6`; D2 n/a here).
3. ✅ `ManageCategoriesDialog`, `ManageConversationsDialog`, `PhraseVersionsDialog` (D1) — done.
4. ✅ `RecorderDialog` (D2) — done. E2 partial (a Discard confirm dialog, if one is ever added)
   still open.
5. Everything else — no open audit findings. (Separately, `ManageCategoriesDialog` and
   `ManageConversationsDialog` also received an owner UX rework batch beyond this audit's
   scope — see the plan and their review notes.)

## Legacy / unused styles

None found. `Theme/Tokens.xaml`, `Tokens.Dark.xaml`, `Tokens.Light.xaml`, `Controls.xaml` are
all current (last touched in the 2026-07-11 redesign) and in active use. No dead style keys
detected in the files read for this audit — a full unused-resource sweep (grepping every
`x:Key` against every `StaticResource`/`DynamicResource` reference) was not run and is out of
scope unless requested.

## Wpf.Ui opportunities

- `ui:ContentDialog` + `IContentDialogService` for E2 (see CTRL-008 in the dotnet-wpf-design
  skill for the exact guard-before-mutate pattern to follow for the delete-confirm case).
- `Appearance="Danger"` for D1 (CTRL-002).
- No other opportunities found — the app already uses `ui:Button`, `ui:TextBox`, `ui:TitleBar`,
  `ui:Card`, `ui:SnackbarPresenter` throughout.

## Open questions

1. **E2 scope:** should the single-instance notice and crash-handler `MessageBox` calls in
   `App.xaml.cs` stay native (recommended above), or is there an appetite to theme those too?
   Recommendation: leave them — robustness during a crash outweighs visual polish there.
2. **C1 / responsive layout:** this audit treats the Full/Docked decision as out of scope (it's
   already an open item owned by [ui-ux-localization-scope.md](../../plans/ui-ux-localization-scope.md)).
   Confirm you want it to stay out of scope for this UX pass, or folded in.
3. No other unknowns — the codebase is small enough (9 windows) that this audit is complete,
   not a sample.
