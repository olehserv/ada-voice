# AdaVoice

A local-first Windows desktop app for one operator who repeats the same phrases all day.

She records phrases **once in her own voice**, organizes them, and plays them with one click
during live calls. The audio is routed so the client hears it as if she spoke into her
microphone. Her real voice keeps flowing between phrases, and one key stops playback instantly.

> **Use case:** an online administrator/operator on **Zoho CRM calls in Google Chrome** who
> follows repeated scripts. AdaVoice is an efficiency aid for a real, present human — not call
> automation and not voice cloning. See the [ethics note](#ethics--privacy) below.

## Status

**Design complete and reviewed. Phase 0 go/no-go gate passed on the target machine
(Architecture A confirmed). Phase 1 audio core is partly built — the device seams,
mic passthrough, and phrase player exist with 23 passing tests, and the WASAPI seam is
hardware-validated.**

The single source of live status is **[handoff.md](handoff.md)** — read it first.
Next real step: finish the Phase 1 engine (orchestrator, recorder, device monitor).

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

- **.NET 10 · WPF · C#** (MVVM via CommunityToolkit.Mvvm)
- **NAudio** (WASAPI capture/render, mixing)
- **VB-CABLE** virtual audio device (user-installed; cannot be bundled — licensing)
- **JSON** metadata + **WAV** audio (48 kHz mono), fully **offline**, no cloud, no accounts

## Repository map

```
ada-voice/
├── README.md              You are here
├── handoff.md             Live status: done / in-progress / next / open questions
├── CLAUDE.md              Working agreement for AI-assisted development
├── docs/
│   ├── design/            The design (01–08) + 09 design system + README index
│   ├── plans/             implementation-plan.md · production-readiness-plan.md
│   └── roadmaps/          mvp-roadmap.md (phases, timeline, go/no-go gates)
└── spike/                 Phase 0 throwaway prototype (see spike/README.md)
```

The four planning docs form a ladder, each with one job:

| Doc | Answers |
|-----|---------|
| [handoff.md](handoff.md) | Where are we right now? |
| [roadmap](docs/roadmaps/mvp-roadmap.md) | What order, how long, which gates? |
| [implementation plan](docs/plans/implementation-plan.md) | How do I build each phase? |
| [production-readiness plan](docs/plans/production-readiness-plan.md) | What must be true before she relies on it? |

Start with the [design docs README](docs/design/README.md) for the full picture.

## Phase 0 spike

A throwaway console prototype that tests the riskiest part (mic→CABLE passthrough + phrase
mixing + ducking) on real hardware before any production code is written. Setup, run
instructions, and the test matrix are in **[spike/README.md](spike/README.md)**.

## Ethics & privacy

- AdaVoice plays **pre-recorded phrases of the real operator, who is present and driving the
  conversation**. One trigger = one human decision. No auto-replies, no scheduled or
  unattended playback.
- It never captures the client's side of a call.
- All recordings stay **on the machine** — no network calls, no telemetry.

Details: [design 07 — risks, security & privacy](docs/design/07-risks-security.md).
