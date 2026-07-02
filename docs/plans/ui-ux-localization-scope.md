# AdaVoice — UI/UX pass + localization: scope & slice order

_Written 2026-07-02._ Scopes out the "next action" named in
[handoff.md](../../handoff.md): the remaining Phase 3/4 work from
[implementation-plan.md](implementation-plan.md) and [design 05](../design/05-ui-design.md) that
wasn't covered by the Board library UI or setup-wizard UI builds.

## Why one label was hiding four different jobs

"Full UI/UX pass + localization" reads like one polish pass. It isn't. Two of the four pieces
below are missing **features** (no Settings window exists; backup/export has no UI at all), not
missing visual states. Treating them as one pass risks underscoping the actual work.

## Current-state audit (2026-07-02)

Checked the real `src/AdaVoice.App` tree against [design 05](../design/05-ui-design.md) §1
(window sizing) and §2 (interaction states).

**Full/Docked responsive layout — not started.** No width breakpoint, `VisualStateManager`, or
column-count logic anywhere. Today there is one layout at every width: a category `ComboBox`
filter (closer to the spec's *Docked* dropdown) plus a `WrapPanel` phrase grid that just wraps by
available space, never explicitly 2-col/3-col. `MinWidth`/`MinHeight` (420×560) are already set
and correctly block below-minimum resizing — that part is done.

**Interaction states ([design 05 §2](../design/05-ui-design.md)) — mixed:**

| Area | Status |
|---|---|
| Calibration | ✅ Done |
| First-run empty board | 🟡 Partial — missing the test-call hint text |
| Broken phrase | 🟡 Partial — dims + warns, but click doesn't open a repair dialog |
| Search no-match | 🟡 Partial — no query echo, no Clear-search button |
| Recorder | 🟡 Partial — no live level meter, no "Processing…", no disk-full handling |
| Wizard checks | 🟡 Partial — no per-row spinner, no "skip anyway" escape hatch |
| Category-empty | 🔴 Missing — falls into the generic no-match state |
| Board decode-at-startup | 🔴 Missing — phrases load synchronously, no dim→enable progression |
| Settings device change | 🔴 Missing — **no Settings window exists** |
| Backup/export UI | 🔴 Missing — `BackupService` runs silently at startup; no UI surface at all |

## Slice order

Four independently shippable slices, in this order:

1. **Settings window** — device pickers (mic/cable/monitor + meters), language choice, hotkey
   reassignment, backup/export UI. Biggest unblock: it's also the largest surface of new
   user-facing strings, so it should land before localization.
2. **Interaction-state gaps on the existing Board** — repair dialog for broken phrases,
   category-empty CTA, search Clear button + query echo, recorder level meter + processing state,
   wizard per-row spinner. Contained, low-risk, fits screens that already exist.
3. **Full/Docked responsive layout** — needs a design decision first: does the category rail
   come back at ≥720px, or do we keep the dropdown-only layout and update design 05 to match
   reality? Implementation follows once that's decided.
4. **Localization retrofit (UA/PL/EN)** — `.resx` for every string, completeness test. Done last
   so slices 1–3's new strings aren't localized twice.

## Not blocking

The current single-layout Board is already in real use (operator pilot passed on it). Slice 3
is real debt but not a shipping blocker — sequence it after 1 and 2, not before.
