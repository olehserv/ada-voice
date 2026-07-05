# AdaVoice

A local-first Windows desktop app for one operator who repeats the same phrases all day.

She records phrases **once in her own voice**, organizes them, and plays them with one click
during live calls. The audio is routed so the client hears it as if she spoke into her
microphone. Her real voice keeps flowing between phrases, and one key stops playback instantly.

> **Use case:** an online administrator/operator on **Zoho CRM calls in Google Chrome** who
> follows repeated scripts. AdaVoice is an efficiency aid for a real, present human — not call
> automation and not voice cloning. See the [ethics note](#ethics--privacy) below.

## Status

**The app is built and in daily-use shape.** Audio engine, recorder, phrase library, Board UI,
setup wizard, Settings window, stop hotkey, backups, and export/import are all shipped and
verified on the target machine. 360 tests green across 5 test projects.

The single source of live status is **[handoff.md](handoff.md)** — read it first.
Open work: responsive Full/Docked layout, localization, installer (see the
[roadmap](docs/roadmaps/mvp-roadmap.md)), and the monetization build
(design in [docs/monetize/](docs/monetize/README.md), no code yet).

## How it works (the core idea)

Windows gives no way to write audio *into* a microphone, so AdaVoice uses the free
**VB-CABLE** virtual audio driver as a software "wire":

```
mic + phrase WAV --> AdaVoice (mix + duck) --> CABLE Input ==> CABLE Output --> Chrome / Zoho --> client
                                               (virtual loopback: looks like a mic)
```

Chrome picks **CABLE Output** as its microphone. AdaVoice continuously passes the real mic
through and mixes a phrase in when triggered. Full reasoning:
[design 02 — audio routing](docs/design/02-audio-routing.md).

## Tech stack

- **.NET 10 · WPF · C#** (MVVM via CommunityToolkit.Mvvm, WPF-UI Fluent theme)
- **NAudio** (WASAPI capture/render, mixing) · **Serilog** (rolling file logs)
- **VB-CABLE** virtual audio device (user-installed; cannot be bundled — licensing)
- **JSON** metadata + **WAV** audio (48 kHz mono), fully **offline**, no cloud, no accounts

## Repository map

```
ada-voice/
├── README.md              You are here
├── handoff.md             Live status: done / in-progress / next / open questions
├── CLAUDE.md              Working agreement for AI-assisted development
├── src/                   App code: App (WPF) · Host · Audio · Audio.Wasapi · Core
├── tests/                 5 xUnit test projects (one per src project)
├── tools/                 AudioSeamCheck hardware-check utility
├── spike/                 Phase 0 prototype (gate PASSED — historical record)
└── docs/
    ├── design/            The design (01–08) + 09 design system + README index
    ├── plans/             production-readiness-plan · operator-pilot · ui-ux-localization-scope
    ├── roadmaps/          mvp-roadmap.md (phases, gates, what is left)
    ├── reviews/           2026-07-04 full codebase review (point-in-time)
    ├── monetize/          B2B licensing/billing design (start at its README)
    └── adr/               Architecture Decision Records
```

The planning docs form a ladder, each with one job:

| Doc | Answers |
|-----|---------|
| [handoff.md](handoff.md) | Where are we right now? |
| [roadmap](docs/roadmaps/mvp-roadmap.md) | What order, which gates, what is left? |
| [production-readiness plan](docs/plans/production-readiness-plan.md) | What must be true before she relies on it? |
| [monetize/implementation-roadmap](docs/monetize/implementation-roadmap.md) | How do we build the paid product? |

Start with the [design docs README](docs/design/README.md) for the full picture.

## Ethics & privacy

- AdaVoice plays **pre-recorded phrases of the real operator, who is present and driving the
  conversation**. One trigger = one human decision. No auto-replies, no scheduled or
  unattended playback.
- It never captures the client's side of a call.
- All recordings stay **on the machine** — no network calls, no telemetry.

Details: [design 07 — risks, security & privacy](docs/design/07-risks-security.md).
