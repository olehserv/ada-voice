# AdaVoice — Design Documentation

Design documents for AdaVoice: a local-first Windows desktop app (WPF / .NET 10) that lets an
online operator play pre-recorded phrases in her own voice into the microphone during live
client conversations (Zoho CRM Web in Google Chrome).

**Status:** Design phase complete. No application code exists yet.

## Documents

| File | Contents |
|------|----------|
| [01-overview.md](01-overview.md) | Executive summary, scope, assumptions, confirmed decisions, user flows, requirements |
| [02-audio-routing.md](02-audio-routing.md) | The core technical challenge: routing phrase audio into the microphone. Options analysis and recommended architecture |
| [03-architecture.md](03-architecture.md) | Application architecture, layers, technology choices, alternatives |
| [04-data-storage.md](04-data-storage.md) | Data model, local file/folder structure, backup/export/import |
| [05-ui-design.md](05-ui-design.md) | WPF UI design, localization (UA/PL/EN), keyboard UX, settings |
| [06-audio-engine.md](06-audio-engine.md) | Audio engine internals, recording engine, hotkeys, latency budget |
| [07-risks-security.md](07-risks-security.md) | Error handling, edge cases, security/privacy, legal/ethical notes, risk register |

Implementation plan: [../roadmaps/mvp-roadmap.md](../roadmaps/mvp-roadmap.md)

## Key decisions (locked 2026-06-10)

- **Audio routing:** VB-CABLE virtual device + in-app mic passthrough/mixing (Architecture A). Pure .NET cannot inject into a microphone — a virtual device driver is mandatory.
- **Platform target:** Zoho CRM Web (Zoho Voice softphone) in Google Chrome; wired headset.
- **Ducking:** mic duck while phrase plays and phrase monitor level are both user-configurable live.
- **Library scale:** a few dozen phrases, 5–15 s each → all phrases pre-decoded to RAM.
- **UI languages:** Ukrainian / Polish / English, switchable at runtime (in MVP).
- **Hotkeys:** only the global emergency-stop hotkey in MVP; per-phrase hotkeys deferred.
- **Trigger policy:** starting a new phrase stops the current one (default, configurable).
