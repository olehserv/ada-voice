# UX / Layout / Style Audit — 2026-07-12

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

- **B1 (High):** `SettingsWindow`'s "Done" button sits *inside* the scrollable content — the
  exact "Save hidden below scroll" bug named in the brief. It is **isolated to this one window**
  — every other dialog with a `ScrollViewer` already keeps its footer button outside it.
- **E2 (High):** every confirm/error/info prompt in normal app flow uses the raw Win32
  `MessageBox` (6 call sites in `MainWindow.xaml.cs`), which renders with OS chrome — a jarring
  break from the FluentWindow look everywhere else. This is the single biggest
  "looks like an old enterprise tool" tell in the app.
- **D1 (Medium):** destructive actions (Delete category, Delete conversation, Remove phrase,
  Delete version) all use `Appearance="Secondary"` — visually identical to non-destructive
  actions. No button anywhere uses `Appearance="Danger"`.
- **C1 (Medium):** `MainWindow` has `MinWidth`/`MinHeight` but no `MaxWidth`/`MaxHeight`, and
  ships one layout at every width above the 420 px minimum (the Full/Docked responsive layout
  is a known, already-tracked open item — see
  [ui-ux-localization-scope.md](../../plans/ui-ux-localization-scope.md)). Nothing breaks
  today, but nothing was verified at large window sizes either.
- **F1 (Low):** phrase tile titles wrap without a true 2-line clamp, so unusually long titles
  can make one tile taller than its row-mates in the `WrapPanel`.

Everything else audited (button order, `IsDefault`/`IsCancel`, spacing, focus, keyboard) is
already in good shape — see the full findings below for what was checked and passed.

## Findings

| ID | Area | Severity | File(s) | Fix pattern | Risk |
|----|------|----------|---------|-------------|------|
| B1 | Dialog footer buttons | **High** | `SettingsWindow.xaml` | Move Grid to header/scroll-content/footer 3-row structure; "Done" outside `ScrollViewer` | Low — pure layout move, no logic touched |
| E2 | Visual consistency | **High** | `MainWindow.xaml.cs` (6 sites), `App.xaml.cs` (2 sites) | Replace in-flow `MessageBox.Show` with `ui:ContentDialog` via `IContentDialogService`; **keep** the 2 in `App.xaml.cs` (single-instance notice before any window exists, global crash handler) as `MessageBox` — a `ContentDialog` needs a live `ContentPresenter`/theme host that may not be reliable during a crash | Medium — touches call sites returning `bool`/tuples; must stay `async`, not `async void`, per dotnet-wpf-design skill CTRL-008 |
| D1 | Button semantics | Medium | `ManageCategoriesDialog.xaml`, `ManageConversationsDialog.xaml`, `PhraseVersionsDialog.xaml` | `Appearance="Danger"` on Delete/Remove buttons (CTRL-002 in dotnet-wpf-design skill) | Low — `Appearance` is a single attribute swap |
| C1 | MainWindow resize stability | Medium | `MainWindow.xaml` | Decide + document behavior above 720 px (this is the same open decision already logged in [ui-ux-localization-scope.md](../../plans/ui-ux-localization-scope.md) slice 3 — not a new problem); at minimum verify no clipping/overlap between 420 px and typical desktop widths | Low for verification; Medium if slice 3's responsive layout is pulled forward |
| F1 | Tile height consistency | Low | `MainWindow.xaml` (phrase tile `DataTemplate`) | Either accept variable height (`WrapPanel` already handles it) or add a real clamp (fixed `Height` + `TextTrimming="CharacterEllipsis"` instead of `TextWrapping="Wrap"`) | Low — cosmetic only |
| D2 | `IsDefault` consistency | Low | `RecorderDialog.xaml` | The "Save" button in the pending-take state has no `IsDefault="True"` (unlike `PhraseEditDialog`/`RepairPhraseDialog`, which do); each state's button is mutually exclusive via `Visibility`, so this is safe to add | Low |

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

1. **B1** — Settings footer fix (isolated, high user-visible impact, lowest risk).
2. **E2** — `ContentDialog` migration for in-flow prompts (highest "professional feel" payoff).
3. **D1** — Danger appearance on destructive buttons (trivial, high clarity payoff).
4. **D2** — `IsDefault` on Recorder's Save.
5. **C1** — MainWindow resize verification (investigation now; implementation only if slice 3
   is pulled forward — that's a feature decision for the owner, not a bug fix).
6. **F1** — tile height clamp (cosmetic, defer to the polish pass, Phase 6/7).

## Windows/screens in priority order for modernization passes

1. `SettingsWindow` (B1)
2. `MainWindow` (E2 partial, D2 n/a, C1, F1)
3. `ManageCategoriesDialog`, `ManageConversationsDialog`, `PhraseVersionsDialog` (D1)
4. `RecorderDialog` (D2, E2 partial via Discard confirm if one is added later)
5. Everything else — no open findings.

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
