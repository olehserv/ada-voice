# 01 — Overview, Scope, Requirements

## 1. Executive Summary

AdaVoice is a local-first Windows desktop application for an online operator who repeats the
same spoken phrases many times per day. She records phrases once in her own voice, organizes
them into categories/scripts, and triggers them during live calls with one click. The audio is
routed so the client hears it as if spoken into her microphone, while her real voice continues
to flow normally between phrases.

The core technical constraint: **Windows provides no user-mode API to write audio into a
microphone.** A virtual audio device driver is required. AdaVoice depends on the free
**VB-CABLE** driver and implements continuous mic passthrough + phrase mixing in its own audio
engine (see [02-audio-routing.md](02-audio-routing.md)).

**Stack:** .NET 10, WPF, C#, NAudio (WASAPI), CommunityToolkit.Mvvm, JSON metadata + WAV files.
No cloud, no accounts, fully offline.

## 2. Product Scope

### In scope (v1)

- Phrase recording, re-recording, preview, rename, delete
- Categories/scripts, tags, search/filter
- Instant playback into the virtual mic; instant stop; one phrase at a time
- Continuous hardware-mic passthrough into the virtual mic with configurable ducking
- One-time voice-level calibration so phrases match her live loudness
- Global emergency-stop hotkey (works while Chrome has focus)
- Always-on-top toggle for the main board (usable beside a full-screen Chrome)
- UI in Ukrainian / Polish / English (language change applies on restart)
- Settings: audio devices, duck levels, routing test wizard
- Local storage, daily backup (metadata **and** audio), export/import (zip)

### Out of scope (v1)

- Voice synthesis / cloning
- Call recording (the client's side is never captured)
- Multi-user / profiles
- Per-phrase hotkeys (deferred — see roadmap)
- Runtime (no-restart) language switching, drag-and-drop categorization, trash/auto-purge
  subsystem (all trimmed in review 2026-06-10 — simpler equivalents ship instead)
- Auto-detection of conversation context, telephony integration
- macOS / Linux

## 3. Assumptions

> All assumptions are listed here explicitly. Items marked ⚠ are unverified until the
> Phase 0 spike.

| # | Assumption | Basis |
|---|------------|-------|
| A1 | Windows 10 21H2+ or Windows 11 x64; admin rights available for one-time driver install | Confirmed by user |
| A2 | Single PC, single operator | Confirmed by user |
| A3 | Wired/USB headset (not Bluetooth) | Confirmed by user |
| A4 | Communication tool: Zoho CRM Web in Google Chrome (Zoho Voice / PhoneBridge softphone, WebRTC) | Confirmed by user |
| A5 | ⚠ Zoho's softphone respects Chrome's microphone device selection, so "CABLE Output" can be chosen as mic | Standard WebRTC behavior; verified in Phase 0 |
| A6 | ⚠ Pre-recorded speech survives Chrome + Zoho audio processing intelligibly. Chrome processes mic input at the getUserMedia layer: **noise suppression, echo cancellation, and automatic gain control (AGC)**, with resampling to ~32 kHz mono on desktop. AGC is the most adversarial to this design — it can re-amplify the ducked mic and re-level phrases | Verified against Chrome documentation 2026-06-10; behavior on her account tested in Phase 0 |
| A7 | Library: a few dozen phrases, 5–15 s each; total audio well under 1 GB | Confirmed by user |
| A8 | Employer/platform permits assistive audio tools | **Resolved (2026-06-13):** no agreement with the employer or Zoho is required (employer is loyal). No longer a gate; this does **not** affect the technical unknowns A5/A6, which Phase 0 still measures |
| A9 | VB-CABLE may be installed on the machine | Confirmed by user |
| A10 | Hotkey style preferences deferred; only global STOP needed in MVP | Confirmed by user |
| A11 | ⚠ Latency budgets are design targets, not measurements, until Phase 0 measures mouth-to-Chrome end to end (including VB-CABLE's internal buffering) | Review 2026-06-10 |

## 4. Confirmed Decisions (canonical)

> **This is the single source of truth for project decisions.** Other documents link here.
> Last updated: 2026-06-10 (eng review).

| # | Question | Decision |
|---|----------|----------|
| 1 | Communication platform | Zoho CRM Web in Google Chrome |
| 2 | Headset | Wired |
| 3 | Mic behavior during phrase playback | Ducked; level configurable live (`micDuckDb`, default −12 dB) |
| 4 | Phrase audible in her headphones | Yes, ducked; level configurable live (`monitorPhraseDb`, default −6 dB) |
| 5 | Library size | Few dozen phrases, 5–15 s → all pre-decoded to RAM (on a background thread; buttons enable as phrases become ready) |
| 6 | UI language | UA / PL / EN; choice applies **on restart** (static `.resx`, no dynamic-binding tax) |
| 7 | VB-CABLE + admin | Approved |
| 8 | Per-phrase hotkeys | Deferred to post-MVP; global STOP stays |
| 9 | New trigger vs. current phrase | New trigger stops the current phrase (default; "ignore" mode available as toggle) |
| 10 | Emergency-stop hotkey | **`Pause`** (not Ctrl+Space — IME/layout-switch conflict on multilingual setups); wizard verifies the key exists and tests it live; `Ctrl+F12` fallback |
| 11 | Recording vs. live call | **Recording during calls is not allowed.** Opening the Recorder takes the app OFF AIR (cable output paused, prominent banner); closing restores the live state. Enforced by design, not discipline |
| 12 | Windows communications ducking | Engine opts out programmatically (`SetDuckingPreference` interop) on its cable + monitor sessions; wizard also sets Sound → Communications → "Do nothing" as fallback |
| 13 | Phrase loudness | RMS/loudness-matched to a wizard-calibrated live-mic reference (sets per-phrase `gainDb`); peak ceiling −3 dBFS retained. Calibration re-runnable from Settings |
| 14 | Deletion | No trash subsystem. Delete = confirm dialog; metadata entry removed, WAV kept on disk as an orphan (renamed `deleted-{id}.wav`) — voice recordings are never unrecoverable |
| 15 | Backup | Daily zip includes `library.json`, `settings.json` **and `audio\`** (her voice is the irreplaceable data); keep 7 |
| 16 | Board window | Always-on-top (`Topmost`) toggle in MVP, default ON — no focus hunt mid-call |
| 17 | Architecture fallback | Voicemeeter (Option B) is **rehearsed in Phase 0** (~half a day), so the fallback is known-good, not theoretical. Architecture A stays primary |
| 18 | Crash resilience | `RegisterApplicationRestart` so Windows relaunches after a crash; DEGRADED alarm plays via the **system default output device**, independent of the monitor setting |
| 19 | Installer | Inno Setup, **self-contained .NET 10** (no runtime download for a non-technical user; ~80 MB larger accepted). Code signing explicitly deferred — SmartScreen warning accepted for family use; revisit if ever distributed |
| 20 | Human gates | Employer-permission gate **removed (2026-06-13):** no employer/Zoho agreement needed (employer is loyal). One human gate remains — the supervised half-day operator pilot after Phase 3 (not first contact at Phase 5) |
| 21 | Visual system | WPF-UI library (Fluent), **fixed dark theme**, Segoe UI Variable, tokenized status colors, 4 px grid — canonical in [09-design-system.md](09-design-system.md) (design review 2026-06-10) |
| 22 | Window layouts | Two named layouts: Full (≥720 px) and Docked (420–719 px, rail→dropdown, 2-col grid); min 420×560; Docked is the primary real-world shape |
| 23 | Interaction states | Every feature × state (loading/empty/error/success/partial) specified in [05 §2](05-ui-design.md); first-run empty board is a designed welcome with a primary action |
| 24 | First-call confidence | Wizard ends with a test-call checklist (call a friend/own phone, play 2 phrases) before the first client call — designed bridge over the peak-fear moment |

## 5. Key User Flows

### F1 — First-time setup (once)

1. Install AdaVoice → guided setup wizard.
2. Wizard detects VB-CABLE; if missing, links to the installer and verifies after install.
3. Environment checks: Windows mic privacy allows desktop apps; CABLE shared-mode format is
   48 kHz (offers to fix); **default output device is NOT CABLE Input** (system sounds must
   not reach the client); AdaVoice session not muted in Volume Mixer.
4. Pick hardware mic + monitoring output (headphones). Engine starts passthrough.
5. Voice calibration: "speak normally for 5 seconds" → live-mic RMS reference stored.
6. Stop-hotkey check: verifies `Pause` exists, live press-to-test; offers `Ctrl+F12` fallback.
7. Wizard instructs: in Chrome / Zoho, select microphone **CABLE Output**.
8. Built-in loopback test: speak → level meter shows signal on the cable side → confirm.
9. First-call confidence card: make a test call (own phone / friend via Zoho), play two
   phrases, confirm they sound natural — before the first client call (decision #24).

### F2 — Record a phrase

Recording panel (app goes **OFF AIR** — cable paused, banner shown) → choose category →
Record → speak → Stop → auto-trim silence + loudness-match to calibrated reference →
preview (monitor only) → Save (title, tags) → close panel → back ON AIR.

### F3 — During a live call (critical flow)

```mermaid
sequenceDiagram
    actor Op as Operator
    participant AV as AdaVoice
    participant VC as VB-CABLE
    participant ZV as Zoho Voice (Chrome)
    actor Cl as Client

    Note over AV,VC: Mic passthrough runs continuously
    Op->>AV: Click phrase button (board is Topmost — no window hunt)
    AV->>AV: Duck mic to micDuckDb, mix phrase in
    AV->>VC: Phrase + ducked mic (one stream)
    VC->>ZV: Appears as microphone signal
    ZV->>Cl: Client hears the phrase as her voice
    AV-->>Op: Phrase monitored in headphones (ducked)
    Cl->>ZV: Client replies (normal speaker path, untouched)
    Op->>AV: Pause key (emergency stop)
    AV->>AV: 10 ms fade-out, mic gain restored
    Op->>ZV: Speaks live, mic already flowing
```

### F4 — Reorganize library

Edit dialog per phrase: change category, title, tags. Delete with confirm (file kept as orphan).

### F5 — Backup

Automatic daily zip (metadata + audio). Manual: Settings → Export → single `.zip`;
Import restores on a new PC.

## 6. Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-1 | CRUD phrases: create (record), rename, delete (confirm dialog; WAV kept as orphan), re-record |
| FR-2 | Phrase metadata: title, category, tags, duration, created/updated, file path, gain |
| FR-3 | Categories: create/rename/delete/reorder; a phrase belongs to exactly one category; phrase moved via edit dialog |
| FR-4 | Local preview playback (monitor device only, never into the call) |
| FR-5 | Live playback into virtual mic; trigger-to-cable latency < 100 ms (target ~40 ms app-side; end-to-end measured in Phase 0) |
| FR-6 | Exactly one phrase at a time; new trigger stops current (default) or is ignored (toggle) |
| FR-7 | Emergency stop: global `Pause` hotkey + always-visible button; effective within one audio buffer (~20 ms) with 10 ms fade |
| FR-8 | Continuous mic passthrough with live-configurable ducking while a phrase plays |
| FR-9 | Phrase playback mirrored to headphones at live-configurable monitor level |
| FR-10 | Search by title/tag, keyboard-first (type-to-filter) |
| FR-11 | Settings: capture device, virtual cable device, monitor device, duck levels, stop hotkey, UI language (restart), Topmost toggle, re-run calibration |
| FR-12 | Routing self-test wizard + live status indicators (mic level, cable level, engine state) |
| FR-13 | Daily backup of metadata + audio (keep 7); manual export/import as zip |
| FR-14 | UI fully localized UA/PL/EN; language choice applies on restart |
| FR-15 | Recording mode is mutually exclusive with on-air: Recorder open ⇒ cable output paused + OFF AIR banner |
| FR-16 | Voice-level calibration: wizard measures live-mic RMS; phrase saves loudness-match to it via `gainDb` |
| FR-17 | Engine opts out of Windows communications ducking on its render sessions |
| FR-18 | App registers for OS restart after crash; DEGRADED alarm plays on the system default device regardless of monitor setting |

## 7. Non-Functional Requirements

- **Latency:** phrase trigger → cable < 100 ms app-side (target ~40 ms). Passthrough adds
  **≤ 60 ms target / 80 ms hard ceiling** to the mic path, app-side. VB-CABLE internal
  buffering and Chrome capture buffering add more — the **mouth-to-Chrome end-to-end number
  is measured in Phase 0** and buffer sizes tuned then (A11).
- **Reliability:** survives 8-hour shifts; auto-recovers from device changes; never *silently*
  stops forwarding the mic — DEGRADED state must be loudly visible/audible (system default
  device, not the optional monitor).
- **Footprint:** < 150 MB RAM with full phrase cache (~100 MB worst case at A7 scale).
- **Offline:** zero network calls. **Privacy:** recordings never leave the machine.
- **Install:** single self-contained installer + separate user-driven VB-CABLE install (licensing).
- **Maintainability:** MVVM, audio engine isolated behind interfaces (see
  [08-testing.md](08-testing.md) for the device seams), no driver code, solo-dev friendly.
