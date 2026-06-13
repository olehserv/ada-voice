# AdaVoice — Handoff & Progress

**Live status of the project.** Read this first when you (or a new session) pick the work
back up. It answers one question: *where are we right now?*

- **What it is:** done work, work in progress, anything interrupted, and open questions.
- **What it is not:** the plan (see [implementation plan](docs/plans/implementation-plan.md)),
  the strategy (see [roadmap](docs/roadmaps/mvp-roadmap.md)), or the decision record
  (canonical table in [design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).

_Last updated: 2026-06-13._

---

## Status in one line

**Design complete and reviewed. Phase 0 spike code written but NOT yet run on hardware.
No production app code exists.** Next real step: run the Phase 0 spike against a real Zoho
call.

## Done

- ✅ **Design phase** — 9 docs in [`docs/design/`](docs/design/README.md), eng review + design
  review both CLEARED (2026-06-10).
- ✅ **Canonical decisions** — 24 entries locked ([design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).
- ✅ **A8 permission gate** — resolved 2026-06-13: no employer/Zoho agreement needed
  (employer is loyal). No longer blocks the build.
- ✅ **Phase 0 spike — code** — throwaway console prototype committed under [`spike/`](spike/README.md)
  (mic→duck→mix→CABLE, latency self-test, ducking opt-out interop). ~656 lines. Builds for
  `net10.0-windows`.
- ✅ **Doc structure cleanup** (2026-06-13) — removed the original brief (`1_DESIGN.md`) and
  `TODOS.md`; moved the design system into [`docs/design/09-design-system.md`](docs/design/09-design-system.md);
  added planning docs under [`docs/plans/`](docs/plans/).

## In progress / interrupted

- _Nothing actively in progress._ The project is paused at the Phase 0 boundary, waiting for
  hands-on-hardware testing.

## Next action

**Run the Phase 0 spike on the Windows machine against a real Zoho Voice call.**
Follow [`spike/README.md`](spike/README.md). Record outcomes into a `spike/PHASE0-RESULTS.md`
(template not yet created — ask Claude to generate it). The go/no-go from this gate decides
Phase 1 (Architecture A) vs. the rehearsed Voicemeeter fallback (Architecture B).

## Open questions (resolved only by Phase 0)

These are **technical unknowns**, separate from the now-closed permission question:

- ❓ **A5** — does Zoho/Chrome respect the `CABLE Output` mic selection? *(expected yes; verify)*
- ❓ **A6** — does Chrome's **AGC** pass pre-recorded phrases intelligibly, or re-level them?
  *(the adversarial unknown — test with real recorded speech, not tones)*
- ❓ **A11** — true mouth-to-Chrome latency (app + VB-CABLE buffer + Chrome buffering).
  *(app-side target ~40 ms; end-to-end measured via loopback recording)*
- ❓ Does the `SetDuckingPreference` opt-out hold across repeated call start/stop cycles?

## Deferred / blocked items

- 🔒 **Board design mockups** — run the gstack designer for 3 dark-theme Board variants
  (Full + Docked) against [design 09](docs/design/09-design-system.md). **Blocked on an OpenAI
  API key** (`~/.gstack/openai.json` or `OPENAI_API_KEY`). Best done before Phase 3 builds the
  Board in XAML. _(Carried over from the old TODOS.md, 2026-06-10.)_
- Post-MVP backlog lives in the [roadmap](docs/roadmaps/mvp-roadmap.md#deferred-post-mvp-backlog).
