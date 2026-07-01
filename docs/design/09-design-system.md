# AdaVoice — Design System

Single source of truth for visual decisions. Screens are specified in
[05-ui-design.md](05-ui-design.md); they are built from the
tokens below. Established by design review 2026-06-10.

## Direction

A calm, dark, native Windows utility — OBS/Voicemeeter class, not a SaaS dashboard.
The best design here is invisible: the operator stops noticing the app exists.
Subtraction default: if an element doesn't earn its pixels mid-call, cut it.

## Theme

- **WPF-UI** library (Fluent / Windows 11 style, MIT) — no hand-rolled chrome.
- **Fixed dark theme** (operator preference; one palette, verified once). No light mode,
  no system-following in v1.

## Tokens

| Token | Value | Use |
|---|---|---|
| `Surface.Window` | `#1F1F1F` | Window background |
| `Surface.Raised` | `#2B2B2B` | Buttons, panels, rail |
| `Text.Primary` | `#F0F0F0` | Titles, body |
| `Text.Secondary` | `#A0A0A0` | Durations, metadata, hints |
| `Status.Live` | `#54D262` | LIVE dot + label |
| `Status.OffAir` | `#F2A33C` | OFF AIR banner |
| `Status.Degraded` | `#FF6B6B` | DEGRADED banner, STOP button |
| `Accent` | `#4CC2FF` | Focus, playing-glow, interactive highlights — sparingly |

All text/surface pairs must hold contrast ≥ 4.5:1. Status colors are verified against
`#1F1F1F` and `#2B2B2B`.

## Typography

Segoe UI Variable (platform convention). Sizes in DIPs:

| Role | Size / weight |
|---|---|
| Window / section titles | 20 / 16 semibold |
| Phrase button title | 16 semibold, 2-line clamp + ellipsis, full title in tooltip |
| Status bar | 14 ALL-CAPS |
| Metadata / durations | 12 |

Floor: nothing below 12; status elements never below 14.

## Spacing & shape

- 4 px grid; control padding steps 8 / 12 / 16.
- Corner radius 4 px everywhere — one radius, no mixing.
- Phrase buttons ≥ 96 px tall; all other interactive targets ≥ 32 px.

## Rules

- Category colors **fill the whole phrase button** (product-owner decision, 2026-07-01, overriding
  the earlier "phrase buttons stay neutral" rule). Every text mark on a filled button uses one
  auto-contrast brush (black or white, WCAG-picked from the fill) so nothing goes illegible. The
  playing indicator is an Accent **ring** (not a background tint) so it reads over any fill. Colors
  come from a curated palette (`AdaVoice.Core.Domain.ColorPalette`), chosen via a colour dropdown —
  never a typed hex. Tags render as rounded chips on the phrase tile: colored border, colored text,
  on a fixed dark scrim background (not the neutral surface colour) so they stay legible over any
  category fill. Each tag's colour comes from the library's tag registry (`Library.Tags`), assigned
  once from the same palette when the tag is first used, so a tag looks the same everywhere.
- Utility copy only: orientation, status, action. No mood copy. Localized UA/PL/EN, sized
  for the longest locale.
- One accent color; decorative gradients, blobs, icon-circles, and emoji-as-design are
  banned.
- Two named window layouts (Full ≥ 720 px, Docked 420–719 px); minimum 420 × 560;
  per-monitor DPI aware.
