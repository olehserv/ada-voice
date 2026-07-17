# AdaVoice — Production-Readiness Plan

**Question this answers:** *what must be true before the operator relies on this app for her
real workday?*

This is the **release gate** — the definition of "done for production." It cross-cuts the
project's phases; many items are *built* earlier
but only *signed off* here, against real hardware and a real call. Sources: NFRs in
[design 01 §7](../design/01-overview.md#7-non-functional-requirements), edge cases and risks in
[design 07](../design/07-risks-security.md), and the test strategy in
[design 08](../design/08-testing.md).

> **Status (2026-07-05):** in progress. Many items are already built and hardware-verified
> (mutex, crash restart, device-loss recovery, daily backups, corrupt-library recovery,
> Serilog logging), and the operator pilot passed. The boxes stay unchecked on purpose:
> each one is a final sign-off on the target machine, done as the last step before release.
> The 8-hour soak and the clean-VM installer test have not been run yet.

---

## Definition of production-ready

> AdaVoice is production-ready when the operator can complete **a full real workday on it
> without developer help**, the live mic **never silently stops**, and her voice data is
> **recoverable** even after a crash or corrupt file.

The gate is met only when **every box below is checked on the target machine**, not in theory.

## 1. Reliability & recovery (the livelihood-critical part)

- [ ] Mic passthrough never *silently* stops — any failure raises a **DEGRADED** state that is
      loudly visible **and** audible on the **system default device** (not the optional monitor).
- [ ] Device unplug/replug mid-call auto-recovers (watchdog + rebuild with backoff).
- [ ] App crash → Windows relaunches it (`RegisterApplicationRestart`); engine auto-restores;
      "recovered" toast shown; unclean exit logged via heartbeat.
- [ ] 8-hour soak passes: drift events logged < a few/hour; RSS flat (no leak).
- [ ] Render-pull stall > 500 ms triggers exactly one rebuild.
- [ ] Two-instance launch is safe: the second instance shows a message and exits
      (single-instance mutex; shipped behavior — it does not focus the first window).

## 2. Audio quality & latency (hardware-verified, not designed)

Run the [manual call-test checklist, design 08 §4](../design/08-testing.md) against a real Zoho
call. The gating subset:

- [ ] Far end hears phrases clearly — intelligibility unaffected by Chrome NS/EC/**AGC** (A6).
- [ ] Live voice between phrases is clean; no level jump at phrase boundaries.
- [ ] Ducked mic actually sounds ducked to the far end, post-AGC.
- [ ] Trigger→cable < 100 ms; **mouth-to-Chrome latency measured and recorded** (A11).
- [ ] Communications-ducking opt-out holds across repeated call start/stop cycles.
- [ ] Mid-call fallback rehearsed: switch Chrome mic to the hardware headset without reconnect.

## 3. Test coverage & CI

- [ ] Unit suites green: state machine (every transition), mixer, ducking ramps,
      single-playback, OFF AIR, watchdog, drift policy.
- [ ] Golden-file DSP suite green: trim, loudness-match (RMS ±0.5 dB, peak ≤ −3 dBFS), fade.
- [ ] Storage suite green: atomic-write kill-9 simulation, corrupt-file recovery, orphan,
      export→import round-trip.
- [ ] Services suite green: hotkey conflict surfacing, **localization completeness (uk/pl/en)**.
- [ ] CI runs unit + golden-file suites on every commit and is green on the release commit.

## 4. Data safety

- [ ] Daily backup zip includes `library.json`, `settings.json`, **and `audio/`**; keeps 7.
- [ ] Restore verified: import a backup zip on a clean machine reproduces the library losslessly.
- [ ] Corrupt `library.json` recovers from newest backup at startup — never starts silently empty.
- [ ] Delete keeps the WAV as `deleted-{id}.wav` (voice recordings never unrecoverable).
- [ ] Disk-full mid-recording aborts the take cleanly; temp file removed; library untouched.

## 5. Observability

- [ ] Serilog rolling-file logging in place; audio failures, drift events, and state changes
      are diagnosable post-hoc.
- [ ] Engine-state alarms wired (DEGRADED audible regardless of monitor setting).
- [ ] Log location documented in the user guide for support.

## 6. Security & privacy

- [ ] Zero network calls (offline verified — no telemetry, no accounts).
- [ ] Recordings and metadata stay under `%LOCALAPPDATA%\AdaVoice\`.
- [ ] Backups/exports are unencrypted zips — **documented to the user** (encryption is post-MVP).
- [ ] App never captures the client's side of a conversation.
- [ ] Unsigned-installer SmartScreen warning is **documented with a screenshot** (decision #19).

## 7. Installation & distribution

- [ ] Inno Setup, **self-contained .NET 10** installer succeeds on a **clean Windows VM** with
      no runtime download.
- [ ] Setup wizard passes all environment checks on a clean machine: VB-CABLE detect/verify,
      mic privacy, cable = 48 kHz, default output ≠ CABLE Input, session not muted,
      Communications = "Do nothing".
- [ ] Loopback self-test and first-call confidence card (decision #24) work end-to-end.
- [ ] VB-CABLE install is wizard-guided (cannot be bundled — licensing).

## 8. Documentation & support

- [ ] User guide: setup, the **fallback playbook** ([design 07 §5](../design/07-risks-security.md)),
      Zoho mic-selection screenshots, SmartScreen note, where logs live.
- [ ] [handoff.md](../../handoff.md) reflects final state.

## 9. Acceptance (the only user)

- [ ] Post-Phase-3 supervised pilot findings addressed (button sizes, duck defaults, category
      workflow, Topmost ergonomics, OFF AIR clarity).
- [ ] **Final acceptance:** the operator completes a full real workday on AdaVoice without
      developer help (Phase 5 exit).

---

## Sign-off

Release v1 only when sections 1–9 are fully checked **on the target machine**. Any unchecked
livelihood-critical item (§1) or hardware audio item (§2) is a hard blocker, not a known issue.
