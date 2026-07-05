# 03 — Application Architecture & Technology Choices

## 1. Architecture overview

Single process, layered, MVVM. The audio core is UI-independent and testable.

```mermaid
flowchart TB
    subgraph UI["WPF UI layer (AdaVoice.App)"]
        V["Views (XAML)<br/>Board · Settings · Wizard"]
    end
    subgraph VM["ViewModel layer (CommunityToolkit.Mvvm)"]
        BVM["BoardViewModel<br/>(also drives recording)"]
        SVM["SettingsWindowViewModel"]
        WVM["SetupWizardViewModel"]
        STVM["StatusViewModel"]
        HOT["HotkeyService<br/>(RegisterHotKey)"]
    end
    subgraph HOST["Composition root (AdaVoice.Host)"]
        EH["EngineHost<br/>ISettingsHost · ILibraryHost · IPlaybackHost …"]
    end
    subgraph SVC["Application services (AdaVoice.Core)"]
        LIB["PhraseLibraryService"]
        BAK["BackupService"]
        SET["JsonSettingsRepository"]
    end
    subgraph CORE["Audio core (AdaVoice.Audio / .Audio.Wasapi — no UI dependencies)"]
        ENG["AudioEngine<br/>graph lifecycle, watchdog"]
        PASS["MicPassthrough"]
        PLAY["PhrasePlayer"]
        REC["Recorder"]
        DEV["WasapiDeviceMonitor<br/>(IMMNotificationClient)"]
    end
    subgraph STORE["Storage"]
        JSON["library.json / settings.json<br/>(atomic writes)"]
        WAV["audio/*.wav"]
        ZIP["backups/*.zip"]
    end

    V --> VM --> HOST
    HOST --> SVC
    HOST --> CORE
    SVC --> STORE
    CORE --> WAV
    DEV --> ENG
```

There is no localization service yet (planned — last UI slice) and no separate
RecorderViewModel: the Board owns the recording flow.

### Rules

- **Audio core runs on dedicated threads.** The UI talks to it through a thread-safe
  command/event interface; no audio work ever runs on the WPF dispatcher.
- `AudioEngine` owns exactly three streams, all kept open for the app's lifetime:
  one capture (hardware mic), one render (CABLE Input), and one render for the DEGRADED
  alarm (system default output). The headphone-monitor render stream is planned — not built;
  previews play to the default output.
- The global stop hotkey is registered system-wide so it fires while Chrome is focused.
- Storage is behind a repository interface so JSON can be swapped for SQLite without touching
  ViewModels (see alternatives below).

### Projects (as built)

```
AdaVoice.sln
├── AdaVoice.App            WPF UI (views, ViewModels, HotkeyService)
│   └── AdaVoice.Host       EngineHost — composition root; ISettingsHost etc.
│       ├── AdaVoice.Audio         engine, passthrough, player, recorder, setup checks
│       ├── AdaVoice.Audio.Wasapi  real WASAPI devices, device monitor, environment probe
│       └── AdaVoice.Core          domain, library/settings/backup services, JSON storage
└── tests/                  5 matching test projects (one per src project)
```

CI runs on GitHub Actions (`.github/workflows/ci.yml`).

## 2. Technology choices

| Concern | Choice | Rationale |
|---|---|---|
| Runtime / UI | .NET 10, WPF, C# | Per project brief; mature Windows desktop stack |
| MVVM | CommunityToolkit.Mvvm | Source-generated observables/commands, lightweight |
| Audio I/O | **NAudio 2.x** | WASAPI capture/render, `MixingSampleProvider`; battle-tested, MIT license. WAV storage goes through the project's own `WavFile.Save` (float → 16-bit PCM, atomic temp→final write) |
| Virtual device | **VB-CABLE** | De-facto standard; free; cannot be bundled (user-driven install via wizard) |
| Metadata | JSON files, atomic write (tmp + rename) | Solo-dev simple, human-recoverable, trivially backed up |
| Audio format | **WAV PCM 16-bit / 48 kHz / mono** | Zero decode latency, no licensing, matches engine format. ~5.6 MB/min — irrelevant at 5–15 s per phrase. MP3 only as a future *export* option |
| Localization | Static `.resx` per language (uk/pl/en) — **planned, last UI slice** | Language choice applies on restart — avoids the `DynamicResource` binding tax runtime switching would impose. Today the app is English-only: no `.resx` files exist yet and UI strings are hard-coded; the retrofit moves them into `.resx` |
| Hotkeys | Win32 `RegisterHotKey` via `HwndSource` | System-wide, simpler and AV-friendlier than low-level keyboard hooks. Default stop key: `Pause` (see decision #10) |
| Ducking opt-out | `IAudioSessionControl2::SetDuckingPreference` COM interop (~30 lines) | NAudio doesn't wrap it; required so Windows doesn't attenuate the cable stream when a call starts |
| Crash resilience | `RegisterApplicationRestart` | Windows relaunches the app after a crash — the mic-forwarding process must not stay dead |
| Logging | Serilog → rolling file | Post-hoc diagnosis of audio issues |
| Installer | Inno Setup, **self-contained .NET 10 publish** | No runtime download on a clean machine (non-technical user); ~80 MB larger accepted. Code signing deferred (documented decision #19) |

### WAV vs. MP3 tradeoff (explicit)

- **WAV (chosen):** no decode step on the hot path, no patent/licensing concerns, lossless
  re-edit (re-record/trim without generation loss). Cost: ~10× disk vs. MP3 — irrelevant at
  this library size (worst case tens of MB total).
- **MP3:** saves disk nobody needs here, adds decoder dependency and quality loss on every
  re-edit. Rejected for storage; acceptable later as an export format.

## 3. Alternative architectures considered

| Alternative | When it would win | Why not now |
|---|---|---|
| Voicemeeter does the mixing (routing Option B) | If in-app passthrough ever proves flaky in real use — **was spiked in Phase 0, so this switch is rehearsed** | Operator must manage a second complex app; A stays primary while B is known-good standby |
| No passthrough; Windows "Listen to this device" | Minimal app complexity | +50–150 ms latency; hidden fragile OS settings |
| SQLite instead of JSON | Library grows beyond ~1–2k phrases or rich querying appears | Overkill at few-dozen scale; repository interface keeps migration trivial |
| Two-process split (audio service + UI) | Hardening: UI crash would no longer kill the mic | Over-engineering for v1; listed as future enhancement |

## 4. Threading model

| Thread | Work |
|---|---|
| WPF dispatcher | UI only |
| WASAPI capture callback | Mic buffer fill (driver-paced, event-driven) |
| WASAPI render callback(s) | Mixer pull → cable / alarm (driver-paced); monitor when that path lands |
| Engine control thread | Start/stop/rebuild commands, device-change handling, watchdog ticks |
| Background | Library save, backup zip, log flush |

Cross-thread communication: immutable command objects into a concurrent queue (UI → engine);
engine state changes surfaced as events marshalled to the dispatcher (engine → UI).
