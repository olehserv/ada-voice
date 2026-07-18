# AdaVoice — Design System

Single source of truth for visual decisions. Screens are specified in
[05-ui-design.md](05-ui-design.md); they are built from the tokens below.
Established 2026-06-10; "Studio Graphite" refresh 2026-07-11; **"Pine Signal"
brand redesign approved 2026-07-18** (owner pick from the three mockups in
[mockups/](mockups/README.md)).

> **Status:** Pine Signal is the *approved target*. Visual reference:
> [mockups/final-pine-signal.html](mockups/final-pine-signal.html). The shipped app still
> renders Studio Graphite until the implementation plan lands — see
> [plans/brand-redesign-implementation-plan.md](plans/brand-redesign-implementation-plan.md)
> and [handoff.md](../../handoff.md).

## Direction

**"Pine Signal" (owner decision 2026-07-18 — replaces "Studio Graphite").** A mix of
mockup variant 3 (Scarlet Pine base: pine chrome, warm off-whites, cream light theme,
bold shapes) with variant 2's gradient window and glow effects, plus one owner idea that
became the signature: **the window itself signals engine state.** The window background
is a deep, quiet gradient tinted by state — green when LIVE, amber when OFF AIR, red
when DEGRADED, grey when STOPPED — so the operator reads the room, not a widget.

The operator-first principles stay: invisible when working, colour is a marker not a
fill, red never decorates. Green is the brand (chrome, ready/positive actions, LIVE).
Red means *hot or destructive* (Record, STOP, DEGRADED, delete). The two brand colours
meet in exactly one decorative place: the 2 px title-bar hairline.

## Theme

- **WPF-UI** library (Fluent / Windows 11 style, MIT) — no hand-rolled chrome.
- **Light + dark, following the OS** (unchanged). Colour tokens in
  `Theme/Tokens.Dark.xaml` / `Tokens.Light.xaml` (identical keys); views bind colours
  with `DynamicResource`, invariant tokens with `StaticResource`; every window is a
  `FluentWindow`.
- **`Accent` stays a `SolidColorBrush`** — `App.SyncBrandLayer` pattern-matches
  `SolidColorBrush` before feeding `ApplicationAccentColorManager` (2026-07-18
  architecture review). Gradient looks live in separate `*.Gradient` tokens; never
  turn `Accent` itself into a gradient.
- **WPF-UI semantic appearances cannot be re-pointed.** `ui:Button`'s `Appearance="Danger"/
  "Success"/"Caution"` coloring is baked into a ControlTemplate trigger with a literal value
  (confirmed 2026-07-18 via `DependencyPropertyHelper.GetValueSource` returning
  `BaseValueSource=TemplateTrigger` at runtime) — it does not read WPF-UI's
  `SystemFillColor*Brush` keys, so no resource-dictionary override can brand it (an earlier
  version of this doc claimed otherwise; a `Theme/WpfUi.Overrides.xaml` was built and proven
  to have zero effect on any real button). Every screen that needs a brand-red/green/amber
  button skips `Appearance=` and sets `Background`/`Foreground`/`BorderBrush` directly via a
  custom style against our own tokens (`Danger.Solid`, `Status.Live`, `Status.OffAir`).
- **`Appearance="Primary"` washes out too, for a different reason** (found 2026-07-18,
  Phase B). `SystemAccentColor` correctly tracks `Accent` (confirmed via a resource dump),
  but WPF-UI's Fluent tint-ramp generator (`AccentFillColorDefaultBrush`) computes a
  *lighter* variant assuming a moderate-lightness base accent (e.g. Windows blue `#0078D4`);
  our brand green (`#7BC96A`) is already light, so the generated "default fill" washes to
  near-white (`#F0F4EF`) in both themes. Same fix as above: skip `Appearance="Primary"`, set
  `Background="{DynamicResource Brand.Gradient}"` directly. Fixed on MainWindow's Start
  toggle; still open on 5 other screens (`CalibrationStepView`, `RecorderDialog`,
  `SetupWizardWindow`, `RepairPhraseDialog`, `PhraseEditDialog`) — fix each when Phase C
  reworks it.
- **No colour literals outside `Theme/`** — including converters and code-behind
  (the 2026-07-18 audit found two frozen hex brushes in `Converters.cs`; that class of
  leak is now explicitly in scope for this rule).

## Tokens

Canonical implementation: `src/AdaVoice.App/Theme/`. The table shows **dark** values;
the light file mirrors every key. All pairs below were script-verified ≥ 4.5:1
(2026-07-18), including gradient stops.

| Token | Dark | Light | Use |
|---|---|---|---|
| `Surface.Window.*` | state gradients (below) | state washes (below) | Window background, per engine state |
| `Surface.Raised` | `#1A241A` | `#FFFFFF` | Panels, tiles, cards |
| `Surface.Overlay` | `#232F23` | `#F0EBDF` | Chips, inputs, elevated bits |
| `Border.Subtle` | `#17F4F1EA` | `#21221E17` | Panel outlines |
| `Border.Strong` | `#2EF4F1EA` | `#3D221E17` | Outlined ghost elements |
| `Text.Primary` | `#F4F1EA` | `#221E17` | Titles, body (warm off-white / ink) |
| `Text.Secondary` | `#ADB8A9` | `#55503F` | Labels, hints |
| `Text.Muted` | `#8E9A8A` | `#6B6552` | Metadata, footnotes |
| `Accent` (brand green) | `#7BC96A` | `#2E5D3A` | Focus, playing tile, accents — solid only |
| `Brand.Deep` | `#2E7D4F` | `#2E5D3A` | Filled green buttons (white text, 5.1:1 / 7.7:1) |
| `Brand.Gradient` | 135° `#2E7D4F→#1F6B41` | flat `#2E5D3A` | Primary CTA faces (Record when idle is NOT primary — see rules) |
| `Status.Live` / `.Tint` | `#7BC96A` / 13% | `#2E5D3A` / 10% | LIVE pill, dot |
| `Status.OffAir` / `.Tint` | `#E8B04B` / 14% | `#8A5A00` / 12% | OFF AIR pill, amber toggle |
| `Status.Degraded` / `.Tint` | `#FF7A70` / 14% | `#B3362C` / 10% | DEGRADED pill, errors, broken-tile warning, recording dot |
| `Status.Stopped` / `.Tint` | `#8E9A8A` / 10% | `#6B6552` / 8% | STOPPED pill |
| `Danger.Solid` | `#C63C34` | `#B3362C` | STOP button, filled destructive (white text 5.1:1 / 6.0:1) |
| `Overlay.Hover` / `.Pressed` | 8% / 5% white | 8% / 5% ink | Hover/press washes |
| `Scrim.Tag` | `#6B000000` | `#C2000000` | Dark scrim behind tag chips; chip text is `#F4F1EA` in both themes, so the light scrim must be denser (76%) to hold AA over white tiles (verified 9.6:1) |

## Gradients

Gradients appear in exactly three places: the window background, `Brand.Gradient`
CTA faces, and the 2 px title-bar hairline (90° `#2E7D4F → #3E5D3A → #C63C34`).
Never under body text without a panel on top.

**State-lit window (the signature).** 165° two-stop linear gradient plus one soft
radial bloom top-left (a decorative Border with a `RadialGradientBrush`, ~320 px,
never behind unpaneled text). On state change the window cross-fades (two stacked
gradient layers, `Opacity` crossfade, 500 ms — Storyboard-safe, no brush animation).

| State | Dark gradient | Dark bloom | Light wash |
|---|---|---|---|
| LIVE | `#111C14 → #0C110E` | `rgba(47,191,113,0.16)` | `#F3F7EE → #FAF6EF` |
| OFF AIR | `#1C170D → #12100A` | `rgba(232,176,75,0.14)` | `#FAF3E3 → #FAF6EF` |
| DEGRADED | `#1D1210 → #120D0C` | `rgba(255,122,112,0.13)` | `#FAEFEC → #FAF6EF` |
| STOPPED | `#141614 → #0F110F` | none | `#F7F5F0 → #FAF6EF` |

All four are near-black (dark) / near-cream (light): `Text.Primary` holds ≥ 14.7:1 on
every stop, and panels sit on `Surface.Raised` anyway. The tint is ambient, not a fill —
if a screenshot reads "the app turned red", the tint is too strong.

## Typography

Segoe UI Variable (platform convention — no new fonts). Sizes in DIPs:

| Role | Size / weight |
|---|---|
| Window / section titles | 20 / 16 semibold |
| Phrase tile title | **15 semibold**, 2-line clamp + ellipsis, full title in tooltip |
| Status pill | 12 bold, ALL-CAPS, +9% letter-spacing |
| STOP button | 21 extra-bold, +14% letter-spacing |
| Metadata / duration chips | 12, tabular numerals |

Floor: nothing below 12; status elements never below 14 (the pill's cap height +
spacing reads larger than its point size — keep the pill ≥ 24 px tall).

## Spacing & shape

- 4 px grid; panel padding via `Pad.Panel` (14,12) — unchanged.
- Radius scale (chunkier than Studio Graphite): `Radius.Small` 4 (tag chips, swatches),
  `Radius.Control` **10** (buttons, inputs, tiles), `Radius.Panel` **14** (panels),
  `Radius.Pill` fully-round (status pill, duration chip).
- Category marker is a **5 px ribbon** down the tile's left edge (was 2–3 px).
- Phrase tiles are **fixed-size** (Pass 6 spec folds in here): constant `Width`/`Height`
  regardless of tag count, title clamp, capped tags + "+N" overflow chip.
- Interactive targets ≥ 32 px, mixed rows share one explicit `Height` (rule 5 of
  [wpf-ux-design-rules.md](wpf-ux-design-rules.md)).

## Elevation & glow

- Depth = surface ramp + `Border.Subtle` outlines. No shadows on flat panels.
- **Glows are state signals, never decoration** (`DropShadowEffect`, `ShadowDepth=0`,
  animate only its `Opacity`): the LIVE pill dot, the recording dot, and the armed
  STOP on hover. Maximum one glowing element per window at rest.

## Motion

Motion tokens live in `Theme/Tokens.xaml`; every animation uses them — no ad-hoc
durations. Storyboard-safe rule (hard): animate only `Opacity`, `RenderTransform`
sub-properties, inline-declared brush `Color`, and `Effect.Opacity`. Never a
container's `Width`/`Height`/`Margin`; never a shared `{StaticResource}` brush.

| Token | Value | Used for |
|---|---|---|
| `Motion.Fast` | 120 ms | Press feedback |
| `Motion.Base` | 160 ms | Hover, focus, colour washes |
| `Motion.State` | 500 ms | Window state-gradient crossfade |
| Standard easing | KeySpline `0.2,0 0,1` (ease-out) | All enters |
| State easing | ease-in-out | Window crossfade |

| Element | Animation |
|---|---|
| Tile hover | Ribbon widens `ScaleX 1→1.6` (transform, origin left) + `Overlay.Hover` wash fades in, 160 ms |
| Tile press | `Scale 0.975`, 120 ms |
| Playing tile | `Accent` border + `Status.Live.Tint` fill — static; the tint is the signal |
| LIVE pill dot | Opacity breathe 1 → 0.5, 1.8 s loop (stops when state ≠ Live) |
| DEGRADED pill dot | Hard 2-step blink, 0.8 s (`DiscreteDoubleKeyFrame`) — "wrong", not "on air" |
| Recording dot | Breathe, 1.2 s |
| Glow in/out | `Effect.Opacity` 0→1, 200 ms |
| Toast | Fade + 8 px rise (`TranslateTransform.Y`), 180 ms enter / ~120 ms exit |

Every looping storyboard is stopped by its trigger's `ExitActions`
(`StopStoryboard`) — state never sticks.

## Interaction states

- **Phrase tiles**: neutral `Surface.Raised`, 5 px category ribbon, title + duration
  chip. Hover = ribbon widen + wash; pressed = scale; **playing** = `Accent` border +
  `Status.Live.Tint` fill (constant border thickness — no layout shift). Conversation
  current-step ring reuses the border slot with `Text.Secondary`; playing wins.
  Broken tile = dimmed + inline `Status.Degraded` warning text, still clickable.
- **Status pill** (new element, replaces the dot-in-button as the primary signal):
  dot + ALL-CAPS state label + state tint, next to the Start/Stop toggle. LIVE adds
  the glow. State is never colour-alone — the label is always visible.
- **Buttons**: one `Primary` per screen. Filled green (`Brand.Deep`/`Brand.Gradient`)
  = commit/ready (Start, Save, Done). Filled red (`Danger.Solid`) = hot or destructive
  (Record while recording, STOP, Delete). Destructive actions always get a confirm
  first (2026-07-18 audit findings 2–3). Quiet window actions stay `Transparent`
  icon buttons with tooltips **and `AutomationProperties.Name`**.
- **OFF AIR toggle** keeps the amber `Caution` treatment while off air.
- **Toasts** unchanged (severity-coloured, bottom-right, 4/6 s) — plus the motion spec.
- **Keyboard** unchanged: `Esc` panic stop, `Ctrl+F` search, global `Pause`. Focus
  visuals come from WPF-UI accent (now brand green).

## Rules

- Category colour = the 5 px left ribbon on a neutral tile. Colours from the curated
  palette (`ColorPalette`), picked via dropdown, never typed hex.
- Tag chips: tag colour on the chip **border**, chip text `#F4F1EA` on the fixed
  `Scrim.Tag` — identical in both themes.
- Status is shown as the pill (dot + ALL-CAPS + tint) **and** the window tint — two
  channels, neither colour-alone.
- **Red budget:** red appears only on Record, STOP, DEGRADED/errors, and destructive
  actions. Green and red never share a role and meet only in the title-bar hairline.
  If a screen shows more than two filled-red elements at rest, the design is wrong
  (audit finding 9).
- Utility copy only; localized UA/PL/EN, sized for the longest locale.
- Icons are Fluent Symbols (`ui:SymbolIcon`) — one icon language; no emoji-as-design.
- Two named window layouts (Full ≥ 720 px, Docked 420–719 px) remain a slice-3 open
  item; minimum 420 × 560; per-monitor DPI aware.
