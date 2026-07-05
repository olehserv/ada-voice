# AdaVoice — Design Documentation

Design documents for AdaVoice: a local-first Windows desktop app (WPF / .NET 10) that lets an
online operator play pre-recorded phrases in her own voice into the microphone during live
client conversations (Zoho CRM Web in Google Chrome).

**Status:** Design phase complete, reviewed (eng review 2026-06-10, incl. independent
outside-voice challenge). The app is now built and in daily-use shape — these documents are
the living design reference. Live status: [handoff.md](../../handoff.md).

## Documents

| File | Contents |
|------|----------|
| [01-overview.md](01-overview.md) | Executive summary, scope, assumptions, **canonical decisions table**, user flows, requirements |
| [02-audio-routing.md](02-audio-routing.md) | The core technical challenge: routing phrase audio into the microphone. Options analysis and recommended architecture |
| [03-architecture.md](03-architecture.md) | Application architecture, layers, technology choices, alternatives |
| [04-data-storage.md](04-data-storage.md) | Data model, local file/folder structure, backup/export/import |
| [05-ui-design.md](05-ui-design.md) | WPF UI design, localization (UA/PL/EN), keyboard UX, settings, wizard |
| [06-audio-engine.md](06-audio-engine.md) | Audio engine internals, recording engine, hotkeys, latency budget |
| [07-risks-security.md](07-risks-security.md) | Error handling, edge cases, security/privacy, legal/ethical notes, risk register |
| [08-testing.md](08-testing.md) | Test strategy: device seams, DSP tests, state-machine tests, manual call checklist |
| [09-design-system.md](09-design-system.md) | **Canonical visual system**: theme, color tokens, typography, spacing, layout rules (what 05's screens are built from) |

Planning docs:
[roadmap](../roadmaps/mvp-roadmap.md) (strategy) ·
[production-readiness plan](../plans/production-readiness-plan.md) (release gate) ·
[handoff.md](../../handoff.md) (live status)

## Key decisions

The **canonical decisions table lives in [01-overview.md §4](01-overview.md#4-confirmed-decisions-canonical)** —
this section is a gist only. In short: VB-CABLE + in-app mixer (Voicemeeter rehearsed as
plan B in Phase 0), Zoho CRM in Chrome with a wired headset, configurable ducking, `Pause`
as the emergency-stop hotkey, UA/PL/EN UI applied on restart, and recording is mutually
exclusive with being on air. The one non-technical gate — a supervised operator pilot after
Phase 3 — **passed 2026-06-29** (the employer-permission gate was resolved 2026-06-13: no
agreement needed).
