# AdaVoice — UI/UX pass + localization: scope & slice order

_Written 2026-07-02; slice statuses updated 2026-07-05._ Scopes out the "next action" named in
[handoff.md](../../handoff.md): the remaining Phase 3/4 UI work from
[design 05](../design/05-ui-design.md) that
wasn't covered by the Board library UI or setup-wizard UI builds.

## Why one label was hiding four different jobs

"Full UI/UX pass + localization" reads like one polish pass. It isn't. Two of the four pieces
below are missing **features** (no Settings window exists; backup/export has no UI at all), not
missing visual states. Treating them as one pass risks underscoping the actual work.

## Current-state audit (2026-07-02 — superseded by slices 1–2)

> This table is the snapshot that justified the slice order. Slices 1 and 2 have shipped
> since: Settings window, backup/export UI, repair dialog, category-empty CTA, search Clear +
> query echo, Recorder Processing state, and the wizard per-check spinner are all done.

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

1. ✅ **Settings window** — shipped 2026-07-02/04 (Levels / Behavior / Language & Backup,
   incl. backup/export UI). Deferred to a future slice: the Devices group (pickers + meters —
   needs live audio metering, which does not exist yet) and true hotkey reassignment.
2. ✅ **Interaction-state gaps on the existing Board** — shipped 2026-07-05, smoke-tested
   2026-07-06 (repair dialog, category-empty CTA, search Clear + query echo, Recorder
   Processing state, wizard per-check spinner). Deferred with the Devices group: the recorder
   live level meter (same missing live-metering capability).
3. ✅ **Full/Docked responsive layout** — resolved 2026-07-20: keep the dropdown-only layout,
   no category rail. The rail (designed 2026-06-10) was superseded by the filter-menu redesign
   (2026-07-07) and, decisively, by Conversations (2026-07-06) — the operator's primary mid-call
   tool is now picking a conversation and following its step highlight, not browsing categories;
   a rail would serve the secondary workflow in the less-common ≥720 px width, not the primary
   Docked strip. Design 05/09/01 updated to match. Verification added: a regression screenshot
   and a live measurement test at the enforced 420 px minimum (the tightest point of the primary
   shape), alongside the existing 1366 px check from Pass 3.
4. 🟡 **Localization retrofit (UA/PL/EN)** — in progress. Stages 1–2 (of 7) done: `.resx`
   infra + full App-layer string extraction (all XAML + ViewModels + code-behind). Stages
   3–7 (Audio/Core code/text split, uk/pl translation, test fixes, theme×language screenshot
   verification, beta.2 re-release) not started. See
   [localization-implementation-plan.md](localization-implementation-plan.md) for the full
   plan, decisions, and exact pickup point.

## Not blocking

The current single-layout Board is already in real use (operator pilot passed on it). Slice 3
is real debt but not a shipping blocker — sequence it after 1 and 2, not before.
