# AdaVoice WPF Client Integration

Purpose: what changes in the WPF app to add licensing — new project, wiring, flows, storage, and exact UX behavior per license state. Status: Proposed, 2026-07-05.

Source of truth for names and values: the canonical monetization brief. Companion docs: `licensing-design.md`, `security-design.md`.

---

## 1. Required WPF changes

Current state: the app has **no** networking, HTTP, auth, crypto, DPAPI, or licensing code anywhere. Everything below is new.

### New project: `src/AdaVoice.Licensing`

- Target: `net10.0-windows`. Referenced **only by `AdaVoice.App`**. Core/Audio/Host stay free of networking.
- Packages: `System.Security.Cryptography.ProtectedData` (DPAPI); JWS verification via built-in `System.Security.Cryptography` ECDsa (or a small JWT library — keep dependencies minimal under central package management in `Directory.Packages.props`).
- Classes (canonical names):
  - `LicenseClient` — all HTTP calls (auth, activation, license issue/refresh).
  - `LicenseTicketValidator` — offline JWS check (the ordered checklist in `licensing-design.md` section 7).
  - `SecureStore` — DPAPI read/write of the `license\*.bin` files.
  - `DeviceIdentity` — deviceId GUID + machineHash computation.
  - `ClockGuard` — `clock.bin` handling, rollback detection.
  - `LicenseStateMachine` — owns the current UX state, raises change events.

### New test project: `tests/AdaVoice.Licensing.Tests`

- xUnit, matching the existing five test projects. Prime targets: `LicenseTicketValidator` (every checklist step, with test-generated ES256 keys), `ClockGuard` edge cases, `LicenseStateMachine` transitions, `SecureStore` round-trips.

### New UI in `AdaVoice.App`

- `LoginWindow` — email + password, shown before `MainWindow` when there is no usable stored auth.
- `LicenseStatusBanner` — non-blocking banner control in `MainWindow` for warning states.
- A full-window blocking view (message + Retry/Reconnect) for blocked states.

### Wiring

- In the `App.xaml.cs` composition root (the app has no DI container — hand-rolled composition there and in `EngineHost`), construct the licensing objects **before `MainWindow` shows**, next to where `EngineHost` is built today.
- Expose one small interface, `ILicenseState`, from the state machine to ViewModels.

Resulting structure:

```
AdaVoice.slnx
├── src/AdaVoice.App          ── references ──► src/AdaVoice.Licensing (NEW)
├── src/AdaVoice.Host                            │ LicenseClient
├── src/AdaVoice.Core                            │ LicenseTicketValidator
├── src/AdaVoice.Audio                           │ SecureStore
├── src/AdaVoice.Audio.Wasapi                    │ DeviceIdentity, ClockGuard
└── tests/AdaVoice.Licensing.Tests (NEW)         │ LicenseStateMachine
```

Sketch of the interface the ViewModels see (final shape decided in implementation):

```csharp
public interface ILicenseState
{
    LicenseUxState Current { get; }        // active, trial, grace_period, ...
    bool IsPremiumEnabled { get; }         // playback-into-call gate
    event EventHandler<LicenseUxState> StateChanged;
}
```

One interface, one gate. ViewModels never talk to `LicenseClient` or the validator directly.

---

## 2. How this respects the existing architecture

- The App → Host → (Core, Audio, Audio.Wasapi) layering is **untouched**. `AdaVoice.Licensing` is a sibling leaf that only `AdaVoice.App` references.
- `Core`, `Audio`, and `Host` stay **offline-pure**: no HTTP, no crypto, no licensing types. They keep their current test story.
- Licensing gates features **at the App/ViewModel layer**. Example: `BoardViewModel` play commands check a single `ILicenseState` (e.g. `IsPremiumEnabled` + current state + change event) in their `CanExecute`. No `if licensed` checks leak into the audio engine.
- Why this is right: the untrusted-client principle (see `security-design.md`) means client checks are UX anyway. Putting them at the ViewModel layer — where UX lives — matches both the architecture and the threat model.

---

## 3. Login flow

**First run** (no `auth.bin`):

1. Show `LoginWindow` before `MainWindow`.
2. `POST /api/auth/login` → access token (in memory) + refresh token → `SecureStore` writes `auth.bin`.
3. `DeviceIdentity` creates the deviceId GUID (`device.bin`) and computes `machineHash`.
4. `POST /api/devices/activate` (with `Idempotency-Key`) → `deviceActivationId`.
5. `POST /api/license/issue` → ticket → validate → `ticket.bin`, update `clock.bin`.
6. Show `MainWindow` in the resulting state.

**Later runs** (auth.bin exists): no login UI. Silent `POST /api/auth/refresh` in the background gets a fresh access token (refresh token rotates; `SecureStore` overwrites `auth.bin`). Login UI reappears only if the refresh token is rejected (`invalid_refresh_token`) or DPAPI unprotect fails.

Error handling during first login:

- `device_limit_reached` at step 4 → show the `device_limit_reached` blocking screen with guidance ("ask your administrator to free a device"). Do not retry automatically.
- Network failure at any step → clear message + Retry. Steps 4–5 carry `Idempotency-Key`, so a retry never burns a second device slot.
- The access token lives in memory only; if it expires mid-flow (15 min), `LicenseClient` silently refreshes and continues.

---

## 4. Device ID and machineHash

- `deviceId`: random GUID on first run, DPAPI-protected in `device.bin`. Not derived from hardware — a reinstall on the same machine is a new device by design (admin can revoke the old one).
- `machineHash`: SHA-256 over normalized soft signals (exact set from the brief):
  - Windows `MachineGuid` (registry `HKLM\SOFTWARE\Microsoft\Cryptography`)
  - machine name
  - Windows user SID
  - system-volume serial
- Normalize (trim, casing, fixed order, separator), concatenate, hash once with SHA-256.
- **Privacy note:** raw signals never leave the machine; only the hash is sent. This should be stated in the privacy documentation for customers (relates to open question 5).
- The hash is recomputed at every local validation; mismatch → require online re-activation (exact match in MVP).

---

## 5. Secure local storage

- All via `SecureStore`: DPAPI `ProtectedData`, **CurrentUser** scope.
- Layout under `%LOCALAPPDATA%\AdaVoice\license\` (sibling to today's `library.json`, `audio/`, `logs/`):

| File | Content |
|---|---|
| `device.bin` | deviceId (GUID) |
| `auth.bin` | refresh token |
| `ticket.bin` | license ticket (JWS compact) |
| `clock.bin` | clock-guard state (`lastAcceptedUtc`) |

- Reuse the repo's existing pattern: atomic temp-then-rename writes and corrupt-file quarantine, same as `JsonPhraseRepository` does today.
- **If DPAPI unprotect fails** (corrupt file, different Windows user, restored image): treat as **first run** for that file. Missing/broken `auth.bin` → show `LoginWindow` (re-login). Broken `ticket.bin` → re-issue online. Broken `device.bin` → new deviceId → re-activation (may consume a device slot; admin can revoke the orphan). Never crash on unprotect failure; log and recover.

---

## 6. Startup validation flow

Ordered, and **never on the UI thread** — async all the way, with a small "checking license" splash state:

1. **Load ticket** — `SecureStore` reads `ticket.bin`.
2. **Local validate** — `LicenseTicketValidator` runs the ordered checklist (signature → aud/iss → deviceId → machineHash → clock guard → expiresAt/graceUntil).
3. **Decide state** — `LicenseStateMachine` sets the UX state; UI proceeds on it immediately.
4. **Background refresh** — fire `POST /api/license/refresh` (startup policy) without blocking anything.

Timing rules:

- The local path (steps 1–3) is milliseconds; the splash state exists so slow disks or first runs do not show a dead window.
- Network calls get a **~5 s timeout**; on timeout, **fall back to the cached ticket** and continue. A slow server must never delay an operator who has a valid cached ticket.
- Audio initialization (`EngineHost`) can proceed in parallel; licensing only gates the play commands, not engine startup.

---

## 7. Online and offline validation

**Online:**

- Refresh on startup and when >50% of the 24 h TTL has passed (~every 12 h), silently in the background.
- A fresh ticket replaces `ticket.bin`; `clock.bin` updates from `serverTime`; the state machine re-evaluates.
- An explicit denial (`subscription_suspended`, `device_revoked`, `tenant_suspended`, ...) switches state immediately — a definitive server "no" overrides grace.

**Offline (or server unreachable / 5xx):**

- Keep the cached ticket. While signature/device/clock checks pass:
  - `now <= expiresAt` → normal state from the ticket's `subscriptionStatus`.
  - `expiresAt < now <= graceUntil` → `offline_allowed` (full app + banner).
  - `now > graceUntil` → `offline_blocked` (premium blocked, Reconnect action).
- Retry refresh with backoff; also update `clock.bin` (`lastAcceptedUtc`) every ~10 min while running.
- Clock rollback beyond the 5-min tolerance → `offline_blocked` until an online refresh clears it.

---

## 8. UX behavior per canonical state

Ukrainian UI note: all user-facing strings go through the existing localization mechanism; the "message intent" column describes meaning, not literal text. In every state: local phrase management (record, edit, organize, search) stays available, and the app **NEVER deletes local data** (`library.json`, `audio/*.wav` are untouched by licensing).

| State | UI form | Message intent | Playback into call (premium) | Phrase management |
|---|---|---|---|---|
| `active` | none | — | enabled | enabled |
| `trial` | banner (info) | "Trial, N days left. How to buy." | enabled | enabled |
| `grace_period` | banner (warning) | "Payment not received. Pay by DATE to keep access." | enabled | enabled |
| `past_due` | banner (warning) | "Invoice overdue. Please pay." | enabled | enabled |
| `suspended` | blocking screen | "Subscription suspended for non-payment. Pay to continue." + Retry | **disabled** | enabled |
| `expired` | blocking screen | "Subscription ended. Renew to continue." + Retry | **disabled** | enabled |
| `device_revoked` | blocking screen | "This device was deactivated by your administrator." + Retry | **disabled** | enabled |
| `device_limit_reached` | blocking screen (at activation) | "Device limit for your plan reached. Free a device or upgrade." + Retry | **disabled** | enabled |
| `offline_allowed` | banner (info) | "Offline mode. Works until DATE. Connect to refresh." | enabled | enabled |
| `offline_blocked` | blocking screen | "Offline too long (or clock problem). Connect to the internet." + Reconnect | **disabled** | enabled |

Design intent: warning states never interrupt an operator mid-call; blocked states are unmistakable, explain the reason, and always offer a one-click Retry/Reconnect that triggers an immediate refresh attempt.

Additional UX rules:

- A state change that *blocks* must never cut audio mid-playback. `LicenseStateMachine` disables the play commands for the *next* action; it does not stop a phrase already playing into a call.
- Banner states are dismissible for the session but reappear on next startup while the state persists.
- The blocking screen never covers the phrase library. Operators can still open, edit, and export their phrases (the existing backup/archive features keep working).
- Retry/Reconnect gives immediate feedback ("checking...") and reports the result within the ~5 s network timeout.

---

## 9. Updater and client security recommendations

- **Authenticode code signing** of installer and executables — recommended; certificate purchase is open question 7. No signing is configured today (`Directory.Build.props` sets Deterministic only).
- **Velopack** auto-updater with signed packages over an HTTPS feed — **Later scope**; do not ship auto-update before code signing exists.
- TLS-only with default certificate validation ON; no pinning in MVP (rotation risk — see `security-design.md` section 10).
- No secrets in the client; pinned *public* keys only. At most light obfuscation of `AdaVoice.Licensing`, and only if piracy is actually observed.

---

## 10. Engineering notes

- **One `HttpClient` singleton** inside `LicenseClient`, created in the composition root. Do not new one per call (socket exhaustion, lost DNS refresh benefits).
- **Retry with jitter** on refresh/heartbeat calls: Polly-style policy or a small hand-rolled helper (exponential backoff + random jitter, capped). Never retry in a tight loop; never retry non-idempotent calls without the `Idempotency-Key` header.
- **No licensing calls on the audio control thread.** All licensing work is async on the thread pool; results marshal to the UI thread only to update `LicenseStateMachine`/bindings. Playback latency is the product; licensing must never sit in that path.
- Log licensing decisions (state transitions, refresh failures, denial codes) through the existing Serilog rolling file — support will live off these lines. Never log tokens or tickets themselves.
- Respect `TreatWarningsAsErrors=true` and central package management when adding the new projects to `AdaVoice.slnx`.
- Keep the single-instance Mutex and `RegisterApplicationRestart` behavior unchanged; after a crash relaunch, the cached ticket path (section 6) restores the license state without user action.
- Testability: `LicenseTicketValidator` and `ClockGuard` take an injected clock (`Func<DateTimeOffset>` or `TimeProvider`) so grace and rollback cases are unit-testable without waiting or touching the machine clock. `SecureStore` gets a base-path parameter so tests never write to the real `%LOCALAPPDATA%`.

---

## 11. MVP vs Later

**MVP:** `AdaVoice.Licensing` project + tests, `LoginWindow`, `LicenseStatusBanner`, blocking view, DPAPI storage, startup validation with background refresh, all ten UX states wired through `ILicenseState`.

**Later:** Velopack updates, component-wise machineHash tolerance, `POST /api/devices/heartbeat` wiring, richer in-app subscription/invoice views, self-serve trial signup UI.
