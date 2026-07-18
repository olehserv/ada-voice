# AdaVoice — Brand redesign mockups (2026-07-18)

> **Decision (owner, 2026-07-18): a mix — variant 3 "Scarlet Pine" base (chrome, shapes,
> cream light theme) + variant 2's gradient window & glow effects, with the window
> gradient **following engine state** (green LIVE / amber OFF AIR / red DEGRADED / grey
> STOPPED). The red ON-AIR lamp was **not** taken — green stays "live". Canonical spec:
> [../09-design-system.md](../09-design-system.md) ("Pine Signal").
> **Final combined mockup: [final-pine-signal.html](final-pine-signal.html)** — shows the
> state-lit window in all four states, both themes, and the full motion spec.

Three design directions for the green + red brand redesign. Each file is one
self-contained HTML page (inline CSS, no external requests) — open it in any browser.
Each recreates the real app screens (board, recorder, dialogs) in dark **and** light
theme, shows hover / press / playing / recording / broken states, and lists the intended
animations with their WPF mapping.

**Every text/surface pair in all three variants was verified ≥ 4.5:1 (WCAG AA)**
with a WCAG relative-luminance script, in both themes. Gradients were checked at their
lightest and darkest stops.

| | [Variant 1 — Emerald Studio](variant-1.html) | [Variant 2 — Verdant Glass](variant-2.html) | [Variant 3 — Scarlet Pine](variant-3.html) |
|---|---|---|---|
| **Mood** | Calm pro tool (OBS-class). Today's app, re-skinned in green. | Premium modern studio software. Gradients + glass + soft glows. | Bold own brand. Broadcast-studio metaphor: red = ON-AIR lamp. |
| **Green** | Emerald accent `#34D399` / `#047857` on green-tinted neutrals | Emerald→teal gradient `#2FBF71→#17A57B` on a deep forest gradient window | Deep pine chrome `#2E7D4F` + pine-lime `#7BC96A`; warm cream light theme |
| **Red** | Reserved: recording, DEGRADED, STOP, destructive only | Same reserve, but red *glows* (recording dot, armed STOP) | Co-lead: LIVE lamp, Record, STOP are red — "red lamp = you are hot" |
| **Christmas risk** | Lowest — red is rare | Low — red only glows when hot | Managed by rule: the two colors meet only in the 2 px title-bar hairline; green stays deep, red stays functional |
| **Gradients** | None (optional 2% window wash) | Window bg, CTA buttons, STOP, playing ring | Only the brand hairline |
| **Motion** | Minimal: 120–180 ms fades, LIVE dot breathe, playing ring pulse | Rich: hover lift + glow, spring press, focus halo (150–200 ms) | Snappy: 120–160 ms, ribbon-widen hover, lamp blink for DEGRADED |
| **Pros** | Safest; keeps "invisible when working"; token swap ≈ done | Most "wow"; clear premium feel; still calm in use | Unmistakable identity; state reading is instant; owns a story |
| **Cons** | Least distinctive — could read as "same app, green" | Glass needs approximation in WPF; glows must stay disciplined | Red LIVE reverses today's green-means-live convention; loudest |
| **WPF effort** | **Low** — token files + small `Controls.xaml` edits | **Medium** — plus button restyle for gradient faces, `DropShadowEffect` glows, no real per-panel blur | **High** — radius/typography token changes, button restyles, lamp element, new tile chrome |

## WPF fidelity flags (details in each file's "WPF fidelity notes")

- **All variants:** animations use only Opacity / RenderTransform / inline-brush Color /
  Effect.Opacity — Storyboard-safe. Status pill = dot + ALL-CAPS text + tint (never color
  alone). WPF-UI `Danger`/`Success` appearances must be re-pointed at the new palette via
  resource-key overrides (see the 2026-07-18 architecture review notes in the audit).
- **Variant 2:** `backdrop-filter: blur()` has no per-element WPF equivalent — panels
  ship as low-alpha fills over the gradient window (colors already chosen for that), or
  the window switches to `WindowBackdropType="Acrylic"`. CSS `border-image` ring is
  *easier* in WPF (`Border.BorderBrush` + `LinearGradientBrush`, keeps corner radius).
- **Variant 3:** ribbon-widen hover must be a `ScaleTransform`, never a `Width`
  animation. The red LIVE lamp is an owner decision — it must be applied everywhere at
  once (pill, dots, docs) to avoid mixed signals.

## Shared decisions across all three (from the 2026-07-18 UX audit)

All variants already include fixes the audit asked for, so the chosen one can ship them:
a real **status pill** with visible state text, a recorder **level meter + timer**,
tamed red in *Manage conversations* (variant 3 shows the reworked layout), fixed-size
tiles with a title clamp and "+N" tag overflow, and duration in tabular numerals.

## Typography

All variants keep **Segoe UI Variable** (platform convention, no new dependency).
They differ only in scale/weight: V1 keeps today's ramp; V2 keeps it with
letter-spaced ALL-CAPS pills; V3 raises tile titles to 15 px semibold and STOP to
21 px extra-bold.
