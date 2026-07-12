# AdaVoice — Design System

Single source of truth for visual decisions. Screens are specified in
[05-ui-design.md](05-ui-design.md); they are built from the
tokens below. Established by design review 2026-06-10; refreshed by the
UI redesign 2026-07-06 ([brief](10-ui-redesign-brief.md)).

## Direction

**"Studio Graphite" (owner redesign 2026-07-11).** A calm, dark-or-light, native Windows
utility — OBS/Voicemeeter class, not a SaaS dashboard. The best design here is invisible:
the operator stops noticing the app exists. Subtraction default: if an element doesn't earn
its pixels mid-call, cut it. **Restraint over loudness: colour is a marker, not a fill.**
Phrase tiles are neutral surfaces with a slim category **edge marker**, not full category
fills — so text is always legible and nothing shouts. One command-center exception: call
state (LIVE / OFF AIR / DEGRADED / STOPPED) reads at a glance — colour, a dot, and a pill;
the one accent is spent on the *playing* tile's ring.

## Theme

- **WPF-UI** library (Fluent / Windows 11 style, MIT) — no hand-rolled chrome.
- **Light + dark, following the OS** (owner decision 2026-07-11 — this **replaced** the
  earlier fixed-dark-only stance). `App.OnStartup` calls `ApplicationThemeManager.ApplySystemTheme`
  then `SystemThemeWatcher.Watch(window)` to follow OS changes live. Colour tokens live in
  `Theme/Tokens.Dark.xaml` / `Tokens.Light.xaml` (identical keys); `App` swaps the active one
  on `ApplicationThemeManager.Changed`. Every text/surface pair holds ≥ 4.5:1 in **both** themes.
- **Views bind theme brushes with `DynamicResource`** so a runtime swap re-resolves them;
  invariant tokens (sizes, radii, spacing) stay `StaticResource`.
- **Every window is a `FluentWindow`** — chrome follows the theme (dark title bars in dark,
  light in light); Board, Settings, wizard, and all dialogs share one chrome.
- **Brand accent is derived from the `Accent` token, not hard-coded.** On each theme change
  `App` reads the theme's `Accent` brush and feeds it to `ApplicationAccentColorManager.Apply`
  — one source of truth in XAML (fixes the old code/XAML accent duplication).

## Tokens

Canonical implementation: `src/AdaVoice.App/Theme/` — `Tokens.xaml` (theme-invariant: sizes,
weights, spacing, radii) plus `Tokens.Dark.xaml` / `Tokens.Light.xaml` (colour brushes, same
keys). No hex literals in view XAML — every colour, radius, and font size comes from a token.
The table below shows the **dark** values; the light file mirrors every key (verified ≥ 4.5:1).

| Token | Value | Use |
|---|---|---|
| `Surface.Window` | `#161719` | Window background |
| `Surface.Raised` | `#202226` | Panels, rail, cards |
| `Surface.Overlay` | `#282B30` | Chips, elevated bits inside panels |
| `Border.Subtle` | `#14FFFFFF` | Panel outlines (depth without shadows) |
| `Border.Strong` | `#26FFFFFF` | Outlined ghost elements |
| `Text.Primary` | `#F2F3F5` | Titles, body |
| `Text.Secondary` | `#A7ACB4` | Labels, hints |
| `Text.Muted` | `#8B919A` | Metadata, footnotes |
| `Status.Live` / `.Tint` | `#54D262` / 14% wash | LIVE dot, label, pill wash |
| `Status.OffAir` / `.Tint` | `#F2A33C` / 15% wash | OFF AIR pill |
| `Status.Degraded` / `.Tint` | `#FF6B6B` / 15% wash | DEGRADED, errors, recording dot |
| `Status.Stopped` / `.Tint` | `#8B919A` / 10% white | STOPPED pill |
| `Accent` | `#4CC2FF` | Focus, playing ring, Primary buttons — sparingly |
| `Overlay.Hover` / `.Pressed` | 9% / 5% white | Tile hover/press wash (pressed dimmer) |
| `Scrim.Tag` | `#66000000` | Fixed dark scrim behind tag chips |

All text/surface pairs must hold contrast ≥ 4.5:1. Status colors are verified against
the dark surfaces.

## Typography

Segoe UI Variable (platform convention). Sizes in DIPs:

| Role | Size / weight |
|---|---|
| Window / section titles | 20 / 16 semibold |
| Phrase button title | 16 semibold, 2-line clamp + ellipsis, full title in tooltip |
| Status bar | 14 ALL-CAPS |
| Metadata / durations | 12, tabular numerals (`Typography.NumeralAlignment`) |

Floor: nothing below 12; status elements never below 14.

## Spacing & shape

- 4 px grid; panel padding via `Pad.Panel` (14,12).
- Radius scale (one scale, no ad-hoc radii): `Radius.Small` 4 (chips, swatches),
  `Radius.Control` 6 (buttons, inputs, tiles), `Radius.Panel` 8 (panels, banners),
  `Radius.Pill` 12 (status pill, tag chips in dialogs).
- Phrase buttons ≥ 96 px tall; all other interactive targets ≥ 32 px.
- Depth = surface ramp + `Border.Subtle` outlines. No shadows on flat panels.

## Interaction states

- **Phrase tiles** (Studio Graphite): a neutral `Surface.Raised` tile with a `Border.Subtle`
  outline and a slim category-colour **edge marker** down the left (not a full fill). Title/
  duration use theme text brushes, so no auto-contrast is needed. hover = `Overlay.Hover`
  wash; pressed = `Overlay.Pressed` (dimmer); **playing** = the tile's 2 px border swaps to
  `Accent` (`PhraseTileFillStyle`) — constant thickness, so the ring never shifts layout. The
  button chrome border stays 0 so WPF-UI's hover border can't imitate the playing ring. The
  Conversations current-step ring reuses the same brush-swap slot with `Text.Secondary`,
  placed above `IsPlaying` so playing wins.
- **Buttons**: WPF-UI appearances — one `Primary` per screen (Start when stopped, Save
  when a take is pending); quiet window actions are `Transparent` icon buttons with
  tooltips and `AutomationProperties.Name`.
- **Toggles show their own state**: the OFF AIR button switches to the amber `Caution`
  appearance while off air (owner decision 2026-07-06 — this replaced the full-width
  OFF AIR banner; the amber status pill remains the second indicator).
- **Notifications are toasts** (owner decision 2026-07-07 — replaced the inline notice
  text): bottom-right of the board area, never over the STOP zone, colored by severity —
  neutral `Secondary` (info), amber `Caution` (warning), red `Danger` (error). Errors stay
  6 s, the rest 4 s. Raised via `BoardViewModel.Notified`; the "Saved ✓" / "Deleted"
  toasts use the same presenter.
- **Keyboard**: `Esc` = panic stop (window-level KeyBinding), `Ctrl+F` = focus search,
  global `Pause` = stop from any app. Focus visuals come from WPF-UI (accent).

## Rules

- Category colour is a **slim edge marker** down the left of the (neutral) phrase tile
  (Studio Graphite, 2026-07-11 — this **replaced** the earlier "colour fills the whole
  button" rule, whose saturated fills read as loud and forced an auto-contrast text brush).
  Text now uses theme brushes and is always legible. The playing indicator is an Accent
  **ring** on the tile border. Colours come from a curated palette
  (`AdaVoice.Core.Domain.ColorPalette`), chosen via a colour dropdown — never a typed hex.
  (`ContrastText` / `ColorContrast.PrefersDarkText` are still used for the colour-swatch
  dropdowns and remain available if a future surface needs on-colour text.)
- Tags render as rounded chips on the phrase tile: the tag's **colour lives on the chip
  border** (its identity); chip **text is `Text.Primary`** on the fixed dark scrim
  (changed 2026-07-06 — colored text failed contrast for dark tag colours). Each tag's
  colour comes from the library's tag registry (`Library.Tags`), assigned once from the
  same palette, so a tag looks the same everywhere.
- Engine state is shown as a **status pill** (dot + ALL-CAPS label + state tint) — the
  dot + text pairing means state is never conveyed by color alone.
- Utility copy only: orientation, status, action. No mood copy. Localized UA/PL/EN, sized
  for the longest locale.
- One accent color; decorative gradients, blobs, icon-circles, and emoji-as-design are
  banned. Icons are Fluent Symbols (`ui:SymbolIcon`) — one icon language.
- Two named window layouts (Full ≥ 720 px, Docked 420–719 px) remain a slice-3 open item;
  the shipped Board uses one layout (search row + filter row) that holds at 420 px and
  has room for the Conversations selector. Minimum 420 × 560; per-monitor DPI aware.
