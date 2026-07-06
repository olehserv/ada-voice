# AdaVoice — Handoff & Progress

**Live status of the project.** Read this first when you (or a new session) pick the work
back up. It answers one question: *where are we right now?*

- **What it is:** done work, work in progress, next steps, and open questions.
- **What it is not:** the strategy (see [roadmap](docs/roadmaps/mvp-roadmap.md)) or the
  decision record (canonical table in
  [design 01 §4](docs/design/01-overview.md#4-confirmed-decisions-canonical)).
- Details of past work live in git history and in the dated docs under `docs/reviews/`.
  This file stays short on purpose.

_Last updated: 2026-07-06._

## Status in one line

**The app is built and verified on the target machine** — engine, recorder, library, Board UI,
setup wizard, Settings window, stop hotkey, backups, export/import; 360 tests green
(72 Core + 97 Audio + 8 Wasapi + 5 Host + 178 App). Slice 2 (interaction-state gaps) is smoke-tested
and confirmed working. Monetization exists as a full design (no code yet).

## Latest work (2026-07-06)

- **Slice 2 smoke test — PASSED.** User ran the full checklist (category-empty CTA, search
  Clear, repair dialog, Processing state, blank-title guard, wizard spinner): "works great, no
  bugs detected." Slice 2 is now fully done, not just shipped.

## Previous work (2026-07-05)

- **Docs consolidation:** deleted the stale `.ukr/` mirror, executed plan/spec checklists
  (`docs/superpowers/`), the two 2026-07-04 fix plans, and the frozen implementation plan.
  Git history keeps them all. Living docs were updated to match the real project state.
- **Monetization design:** full B2B licensing/billing documentation in
  [`docs/monetize/`](docs/monetize/README.md) (start at its README) plus 6 ADRs in
  [`docs/adr/`](docs/adr/). Key decisions: ASP.NET Core backend in a new `server/` folder,
  PostgreSQL + EF Core, ES256-signed 24-hour license tickets with offline grace (7 days paid /
  2 days trial), DPAPI client storage, refresh-token rotation, manual invoice billing v1,
  payment-provider webhooks v2. Next: answer OQ-12/OC-06 (device vs per-seat limits) in
  [open-questions](docs/monetize/open-questions.md), then Phase 0 of the
  [monetize roadmap](docs/monetize/implementation-roadmap.md).
- **Slice 2 (interaction-state gaps) shipped:** repair dialog for broken phrases,
  category-empty CTA, search Clear + query echo, Recorder Processing state + hardened
  `SaveTake` (closes review finding M15), wizard per-check spinner. Fully reviewed;
  360 tests green; pushed to `main`; smoke-tested (see 2026-07-06 above).

## Done (compact history, newest first)

- ✅ **Slice 2 — interaction-state gaps** (2026-07-05): see "Latest work" above.
- ✅ **"Next touch" review fixes** (2026-07-04): engine recovery M4–M7, recording/calibration
  safety M1/M2, transactional import + zip caps M9/M10, WASAPI COM hygiene M13, and two new
  test projects (`Host.Tests`, `Audio.Wasapi.Tests`) with an injectable `EngineHost` (H11).
- ✅ **Top-10 risk fixes** (2026-07-04): all Critical/High findings from the
  [full codebase review](docs/reviews/2026-07-04-full-codebase-review.md) fixed — mic-duck
  relay across rebuilds (C1), read-error write guard (C2), rebuild backoff (H1), global
  exception handlers + crash restart + single-instance mutex (H2/H3/H4), async preview
  (H5/H6), start-error surfaced in the status bar (H7), COM `[PreserveSig]` (H8), import
  re-keys WAVs (H9), drift off the audio threads (H10). Committed and smoke-tested.
- ✅ **Settings window** (2026-07-02/04): Levels (duck slider, re-run calibration), Behavior
  (always-on-top, retrigger toggle, hotkey status), Language & Backup (language picker,
  export/import, backup info). Smoke-tested by the user (a crash-on-open bug was caught and
  fixed). Deferred to a future slice: Devices group (needs live audio metering) and true
  hotkey reassignment.
- ✅ **Board library UI** (2026-07-01/02): edit/delete/search/category filter, category
  manager, colour-filled tiles with WCAG auto-contrast, coloured reusable tag chips, window
  placement memory, test-on-headphones. Smoke-tested by the user.
- ✅ **Setup wizard** (2026-07-02): environment checks (+ VB-CABLE download link), voice
  calibration (+ countdown ring), hotkey status, instructions, first-call card. First-run
  trigger + re-run via **Setup…**. Smoke-tested by the user. v2 follow-up (not started):
  device pickers, live meters, loopback self-test.
- ✅ **Hardware run of the full loop — PASSED** (2026-07-01): engine + recorder + storage +
  preview verified end-to-end on the target machine, including cable unplug/replug recovery.
- ✅ **Global stop hotkey** (2026-07-01): `Pause` (fallback `Ctrl+F12`) via `RegisterHotKey`;
  stops the phrase only, works while Chrome is focused.
- ✅ **Duck slider + WPF-UI Fluent theme** (2026-07-01): live mic-duck slider; dark Fluent
  chrome, design-09 tokens, save toast; Topmost preserved.
- ✅ **Phase 2 storage** (2026-06): categories/tags, delete-as-orphan, daily zip backups +
  recovery, export/import, `settings.json`, corrupt-library quarantine, atomic writes.
- ✅ **Phase 1 audio core** (2026-06): `AudioEngine` state machine (Stopped/Live/OffAir/
  Degraded, rebuild + backoff, watchdog), WASAPI factory + device monitor, `EngineHost`,
  Recorder (trim + loudness match), storage + preview vertical.
- ✅ **Operator pilot — PASSED** (2026-06-29): supervised real-call pilot; user: "tested
  everything, works awesome". Script kept for re-runs:
  [operator-pilot.md](docs/plans/operator-pilot.md).
- ✅ **Phase 0 spike gate — PASSED** (2026-06-15): Architecture A (VB-CABLE + in-app mixer)
  confirmed against a real Zoho call. Results: [spike/PHASE0-RESULTS.md](spike/PHASE0-RESULTS.md)
  (file exists; exact measured latency/AGC numbers were never filled in — still TBD there).
- ✅ **Design phase** (2026-06-10): 9 design docs, eng + design reviews cleared,
  24 canonical decisions locked.

## In progress

Nothing open right now. Slice 2 is done and verified; next work needs a decision first (see below).

## Next action

**UI/UX pass + localization** — remaining slices (scope + rationale in
[ui-ux-localization-scope.md](docs/plans/ui-ux-localization-scope.md)):

1. ✅ Settings window — done, smoke-tested.
2. ✅ Interaction-state gaps — done, smoke-tested.
3. **Full/Docked responsive layout** — not started; needs a design decision first (bring back
   the category rail at ≥720 px, or keep dropdown-only and update design 05).
4. **Localization retrofit (UA/PL/EN)** — last, after slice 3's strings exist. All UI strings
   so far are English-only; a `.resx` retrofit is known debt.

Separately, **monetization** is blocked on the owner answering OQ-12/OC-06 (device vs per-seat
limits) before Phase 0 of the [monetize roadmap](docs/monetize/implementation-roadmap.md) starts.

**Monetization** — next step is Phase 0 of the
[monetize roadmap](docs/monetize/implementation-roadmap.md), after the owner answers
OQ-12/OC-06 (device vs per-seat limits).

## Open follow-ups (named so they're not lost)

- **Configurable monitor device:** preview uses the default output as a stand-in; real
  selection comes with the Settings Devices group.
- **Recorder live level meter + no-signal detection:** deferred until live audio-capture
  polling exists; bundle with the Devices group.
- **2nd-capture fallback:** if a driver refuses a second WASAPI capture client, fall back to
  tapping the engine's capture. Watch for it on hardware.
- **Cold-start auto-retry into Degraded:** a failed `Start` currently stays Stopped with the
  error surfaced.
- **Fill in `spike/PHASE0-RESULTS.md` measured numbers** (latency, AGC notes) if they are
  ever re-measured; the gate itself passed.
- Post-MVP backlog lives in the [roadmap](docs/roadmaps/mvp-roadmap.md#deferred-post-mvp-backlog).
