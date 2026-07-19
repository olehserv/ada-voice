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

**Status: done (2026-07-18).** Shipped: state-lit backdrop (4 gradient layers + 3 blooms,
`Opacity`-switched per `Status.State`, screenshot-verified in all 4 states × both themes),
status pill (dot + ALL-CAPS label + tint + static LIVE glow), fixed `148×128` tile (title
2-line clamp + ellipsis, pill duration chip, "+N" tag overflow, 5 px ribbon), brand-red STOP
fill, and the title-bar hairline. Full details, including several corrections found only by
rendering and pixel-sampling (not assumed from the plan): see
`docs/design/09-design-system.md`'s Theme section and `handoff.md`.

Two findings that affect later phases:
1. **`Appearance="Primary"` also washes out** (not just Danger/Success/Caution from Phase
   A) — WPF-UI's accent-tint ramp assumes a moderate-lightness base accent; our light brand
   green washes its "default fill" tint to near-white. Fixed for MainWindow's Start toggle
   (`EngineToggleButtonStyle`, direct `Background="{DynamicResource Brand.Gradient}"`, no
   `Appearance=`) — **Phase C must apply the same fix** to the 5 other files still using
   `Appearance="Primary"` for a brand-green CTA (`CalibrationStepView`, `RecorderDialog`,
   `SetupWizardWindow`, `RepairPhraseDialog`, `PhraseEditDialog`).
2. **WPF's `TextBlock` cannot ellipsize a wrapped multi-line clamp** (`TextTrimming` +
   `TextWrapping="Wrap"` collapses to single-line-with-ellipsis instead) — a genuine,
   non-obvious WPF limitation (`MaxLines` doesn't exist either; that's WinUI-only). Fixed via
   a new `TitleClampConverter` (measures against a real off-screen `TextBlock`, truncates the
   string itself). Reuse this pattern for any other multi-line clamp Phase C needs.

Motion (crossfade, dot breathe/blink, hover ribbon, STOP glow) stays deferred to Phase D —
the backdrop/pill/tile triggers already switch by `DataTrigger` Setter, ready for Phase D to
add `EnterActions`/`ExitActions` Storyboards to the same triggers with no structural change.

1. ~~**State-lit window**: two stacked gradient Borders...~~ Shipped as 4 gradient + 3 bloom
   `Border`s behind the content Grid (not literally 2 — one per state, `Opacity`-switched).
   Static-only in Phase B; the `Opacity` crossfade itself is Phase D motion.
2. ~~**Status pill**...~~ Shipped as planned.
3. ~~**Tile rework**...~~ Shipped, but tile `Height` is **128, not 106**: a genuinely
   2-line-clamped title left too little room for the duration chip + tags row (confirmed by
   a visibly clipped tag chip in a screenshot). `MaxVisibleTagChips` is **1, not up to 2**:
   2 tags + the overflow chip clip past the tile's rounded-corner content bounds; the
   mockup's own reference tile only ever shows 1 tag + overflow. **A second-opinion review
   caught a spec miss the fixtures never exercised:** the playing tile's `Accent` border had
   no `Status.Live.Tint` fill (both are required per 09/this item — only the border had been
   added) — fixed.
4. ~~STOP zone...~~ Shipped as planned, plus the Start/Primary finding above. **Same review
   also caught that MainWindow's own empty-board "Record"/"Record into…" CTAs were still
   `Appearance="Primary"`** — the exact bug just fixed on the Start toggle, left unfixed in
   the same file. Fixed via a new shared `BrandCtaButtonStyle` (`Controls.xaml`).

## Phase C — dialogs + wizard (folds in the remaining audit findings)

**Status: done (2026-07-19).** All 9 items below shipped, one review-gated step at a time; full
per-step writeup (including 3 items found beyond this plan — a real, still-open light-theme
legibility bug in several dialogs; a `ListBoxItem` selection-theming fix needing a full
`ControlTemplate` override; a color-dropdown alignment fix) in
`C:\Users\olehs\.claude\plans\check-what-is-planned-temporal-kahn.md` and `handoff.md`. Recorder's
live level-meter (item 1 below) stayed deferred — no live-metering capability exists yet; only the
timer + idle guidance shipped for that item.

1. Recorder: level-meter + timer in recording state, idle-state guidance (finding 10),
   button order `Discard (Danger) … Preview … Save` + discard confirm (finding 3). Save
   (currently `Appearance="Primary"`) needs Phase B's fix too — see Phase B item 1.
2. Manage conversations: theme the `ListBox` selection (finding 4), tame red — member
   "✕" → Transparent (finding 9), empty-state hint + wider name column (finding 10),
   delete confirms (finding 2).
3. Manage categories + versions: delete confirms (finding 2); versions dialog reuses
   the tile pattern (audit "also noted").
4. Phrase edit: tag chip / "✕" hit targets ≥ 24–32 px (finding 7). Its Save button
   (`Appearance="Primary"`) needs Phase B's fix too — see Phase B item 1.
5. Repair dialog: Remove → brand red (finding 6, via `Background`, not `Appearance=
   "Danger"` — see Phase A item 2). Wizard: "Step n of 5" (finding 8).
6. Glyph buttons get `ToolTip` + `AutomationProperties.Name` (audit "also noted").

Note: confirms may land as `MessageBox` first if Pass 2b (`ContentDialog` migration)
hasn't shipped — but doing Pass 2b before/with Phase C avoids double work.

**Screenshot-fixture lessons from Phase B:** a populated, non-playing, non-broken board
fixture doesn't exercise every rest-state a spec calls for — Phase B's own playing-tile
fill, empty-board CTA, and broken-tile restructure all shipped un-rendered until a
second-opinion review asked "what never got a screenshot?" Phase C should add a fixture
for every distinct visual STATE a dialog can be in (not just its default/happy-path data),
not only every dialog. Separately: don't mutate `SampleHost()`'s shared phrases for one
test's stress data — a long-title/multi-tag `p-1` (added to verify the tile clamp) also
distorted `ManageConversationsDialog`'s and other dialogs' screenshots that reuse the same
fixture, since `Save()` only asserts `File.Exists`, not that the layout still looks right.
Put stress data on a test-local addition instead.

## Phase D — motion + polish

1. Animations per 09's motion table (hover/press/breathe/blink/glow/toast), every loop
   with `StopStoryboard` exit actions.
2. Full screenshot pass both themes; visual review vs mockups; contrast spot-check on
   the rendered PNGs; full test suite.

## Explicitly out of scope

Full/Docked responsive layout (slice 3, separate decision), localization retrofit,
`MessageBox`→`ContentDialog` migration (Pass 2b — separately tracked, though Phase C
prefers it first).
