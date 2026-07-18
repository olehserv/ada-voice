# Pine Signal brand redesign — implementation plan (2026-07-18)

**Plan only — no code has been written.** Target design:
[09-design-system.md](../09-design-system.md) ("Pine Signal", approved 2026-07-18).
Mockups: [mockups/](../mockups/README.md) — variant 3 base + variant 2 gradient window
and glows + owner's state-lit window idea. Inputs: the 2026-07-18 UX audit (10 findings,
in handoff) and the 2026-07-18 architecture review (token system is restyle-ready; the
leaks are WPF-UI `Appearance` semantics in ~15 places and 2 frozen brushes in
`Converters.cs`).

Every phase ends the same way: build + `xstyler` + screenshot tests in **both** themes
+ owner review, per [wpf-ux-design-rules.md §11](../wpf-ux-design-rules.md). One phase
per review stop.

## Phase A — foundation (tokens + semantic key ownership; no visible redesign yet)

The goal: after Phase A, the whole palette is swappable from `Theme/` alone.

**Status: done (2026-07-18), with one item descoped — see below.**

1. New palette in `Tokens.Dark.xaml` / `Tokens.Light.xaml` (09's table), incl. the four
   `Surface.Window.*` state gradients, `Brand.Deep`, `Brand.Gradient`, `Danger.Solid`,
   `Status.*.Tint`. Keep `Accent` a `SolidColorBrush` (App.SyncBrandLayer pattern-match).
2. ~~New `Theme/WpfUi.Overrides.xaml`...~~ **Descoped — does not work.** `ui:Button`'s
   `Appearance="Danger"/"Success"/"Caution"` coloring is baked into a ControlTemplate
   trigger with a literal value (confirmed via `DependencyPropertyHelper.GetValueSource`
   → `BaseValueSource=TemplateTrigger` at runtime), not a `DynamicResource` read against
   WPF-UI's `SystemFillColor*Brush` keys. A `Theme/WpfUi.Overrides.xaml` re-pointing
   those keys was built, wired into `App.xaml`, and verified via screenshot tests +
   pixel-sampling the rendered PNGs (not just eyeballing — two similar reds fooled a
   first visual check) to have **zero effect on any real button**. Deleted. 09 corrected
   to state this. Brand-coloring Danger/Success/Caution buttons moves to Phase B/C:
   skip `Appearance=` entirely and set `Background`/`Foreground`/`BorderBrush` directly
   via a custom style against `Danger.Solid`/`Status.Live`/`Status.OffAir` — those
   phases already rework STOP/Delete/Discard/Remove, so it's not extra file-touching.
   The `*.Color` token shadows (`Danger.Solid.Color` etc.) added for the abandoned
   override stay — Phase B/C can still use them wherever a raw `Color` is needed.
3. Fix the token bypasses (audit finding 1): `CheckStatusToBrushConverter` →
   `DynamicResource` triggers; retire the stale `#2B2B2B` fallback.
4. Motion + radius tokens in `Tokens.xaml` (`Motion.Fast/Base/State`, easings,
   `Radius.Control`=10, `Radius.Panel`=14).
5. Extract MainWindow's five inline styles (Start/Stop toggle, state dot, OFF AIR,
   Record, hover overlay) into `Controls.xaml` so later phases are token/style-only.

Risk (materialized on item 2, see above): verify at runtime, not just by build — a
green build and even a passing screenshot-test smoke run proved nothing here; only
pixel-sampling the PNG and `GetValueSource` on a live control surfaced the bug.

## Phase B — MainWindow (the board)

1. **State-lit window**: two stacked gradient Borders behind the content Grid,
   `Opacity` crossfade 500 ms on `Status.State` change; radial bloom Border on top,
   per 09's gradient table.
2. **Status pill** (dot + ALL-CAPS label + tint + LIVE glow) next to the Start/Stop
   toggle (audit finding 5). Dot keeps living in the toggle too — two channels.
3. **Tile rework**: fixed size + title clamp + "+N" tag overflow (absorbs Pass 6),
   5 px ribbon + `ScaleX` hover, duration chip, playing = Accent border +
   Live tint fill.
4. STOP zone (`Danger.Solid`, 21 px extra-bold, hover glow) and Record button
   (red only while recording). **Reminder (Phase A finding):** `Appearance="Danger"/
   "Caution"` does not brand-color a `ui:Button` — set `Background`/`Foreground`
   directly against the tokens instead; see Phase A item 2.

## Phase C — dialogs + wizard (folds in the remaining audit findings)

1. Recorder: level-meter + timer in recording state, idle-state guidance (finding 10),
   button order `Discard (Danger) … Preview … Save` + discard confirm (finding 3).
2. Manage conversations: theme the `ListBox` selection (finding 4), tame red — member
   "✕" → Transparent (finding 9), empty-state hint + wider name column (finding 10),
   delete confirms (finding 2).
3. Manage categories + versions: delete confirms (finding 2); versions dialog reuses
   the tile pattern (audit "also noted").
4. Phrase edit: tag chip / "✕" hit targets ≥ 24–32 px (finding 7).
5. Repair dialog: Remove → brand red (finding 6, via `Background`, not `Appearance=
   "Danger"` — see Phase A item 2). Wizard: "Step n of 5" (finding 8).
6. Glyph buttons get `ToolTip` + `AutomationProperties.Name` (audit "also noted").

Note: confirms may land as `MessageBox` first if Pass 2b (`ContentDialog` migration)
hasn't shipped — but doing Pass 2b before/with Phase C avoids double work.

## Phase D — motion + polish

1. Animations per 09's motion table (hover/press/breathe/blink/glow/toast), every loop
   with `StopStoryboard` exit actions.
2. Full screenshot pass both themes; visual review vs mockups; contrast spot-check on
   the rendered PNGs; full test suite.

## Explicitly out of scope

Full/Docked responsive layout (slice 3, separate decision), localization retrofit,
`MessageBox`→`ContentDialog` migration (Pass 2b — separately tracked, though Phase C
prefers it first).
