# Stop Hotkey — Design Spec

_Date: 2026-06-29. Status: approved (brainstorming). Next: implementation plan._

## Problem

The operator must stop a playing phrase **instantly while Chrome has focus** (mid-call). Today
the only stop is the on-screen STOP button, which requires clicking away from the call. A normal
WPF key binding only fires when our app is focused, so it cannot solve this.

This is the canonical **emergency-stop hotkey** (design 01 decision #10, FR-7): global `Pause`,
`Ctrl+F12` fallback, effective within ~one audio buffer with the 10 ms fade.

## Scope

**In this slice:**
- Register a **global** hotkey (works system-wide, incl. when Chrome is focused) via Win32
  `RegisterHotKey` on the Board window's HWND.
- Key policy: try **`Pause`** first; if registration fails (key taken / conflict), automatically
  fall back to **`Ctrl+F12`**.
- On the hotkey, run the **same action as the big STOP button**: `IPlaybackHost.StopPhrase`
  (10 ms fade; the live mic keeps passing through). It does **not** stop the engine.
- If **both** keys fail to register: log it and show a small notice in the status bar. The
  on-screen STOP still works, so the operator is never blocked.
- Unregister + dispose the hotkey when the window closes.

**Out of scope (other slices):**
- Reassignment UI → the **Settings page** slice (design 05 §2 "Behavior").
- Live press-to-test + key-exists check → the **setup wizard** slice (design 01 step 6).
- `RegisterApplicationRestart` → already implemented in the host.

## Architecture

WPF concern (needs the window handle), so it lives in `AdaVoice.App`. The un-testable Win32 part
is isolated behind a seam so the policy + wiring are unit-testable.

```
AdaVoice.App
├── Services/
│   ├── IHotkeyRegistrar.cs   seam: Register(key,mods) -> bool; Unregister(); HotkeyPressed event
│   ├── Win32HotkeyRegistrar.cs   real impl: RegisterHotKey/UnregisterHotKey + HwndSource.AddHook
│   └── HotkeyService.cs      policy: try Pause -> fallback Ctrl+F12; raises StopRequested; exposes the result
└── MainWindow (composition)  creates the service from the HWND, wires StopRequested -> host.StopPhrase,
                              shows the failure notice, disposes on close.
```

### Components

- **`IHotkeyRegistrar`** — the hardware/OS seam. `bool Register(VirtualKey key, Modifiers mods)`,
  `void Unregister()`, and an event raised when the registered hotkey is pressed. The real impl
  wraps `RegisterHotKey`/`UnregisterHotKey` and hooks `WM_HOTKEY` via `HwndSource.AddHook`. A fake
  impl drives tests.
- **`HotkeyService`** — owns the **policy**: attempt the candidates in order
  (`[Pause]`, then `[Ctrl+F12]`), keep the first that registers, expose which one (or "none"),
  and raise `StopRequested` when the registrar signals a press. Knows nothing about phrases.
- **`MainWindow`** — on load: build the registrar from the HWND, build the service, subscribe
  `StopRequested -> (DataContext as BoardViewModel).StopCommand` (i.e. `StopPhrase`). On the
  "none registered" result, set a status notice. On close: dispose.

### Data flow

`Pause pressed (any focus)` -> OS -> `WM_HOTKEY` -> `Win32HotkeyRegistrar.HotkeyPressed`
-> `HotkeyService.StopRequested` -> `MainWindow` -> `IPlaybackHost.StopPhrase` -> engine fades out
the phrase. No engine changes.

## Boundaries / dependencies

- `App -> Core/Host` only (unchanged). `HotkeyService` depends on `IHotkeyRegistrar`, not Win32.
- No changes to the audio engine, host seams, or view-models (the hotkey reuses `StopCommand`).

## Error handling

- `Register` returns false on conflict → service tries the next candidate.
- All candidates fail → service result is `None`; `MainWindow` shows a status notice and logs it;
  the app keeps working with the on-screen STOP.
- Dispose is idempotent and always unregisters.

## Testing

- **Unit (CI):** with a `FakeHotkeyRegistrar` —
  - tries `Pause` first and stops there when it succeeds;
  - falls back to `Ctrl+F12` when `Pause` registration returns false;
  - reports `None` when all candidates fail;
  - a simulated press raises `StopRequested` exactly once.
- **Manual (operator/dev):** play a phrase, focus Chrome, press `Pause` → phrase stops; the
  startup log states which key registered. (Global keypress can't be simulated in CI.)

## Open decision (resolved)

Hotkey stops the **current phrase** (mirrors the big STOP), not the engine — stopping the engine
would cut the live mic mid-call. Reassignment is intentionally deferred to the Settings slice.
