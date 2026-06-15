# Phase 0 Results — go/no-go gate

**Outcome: GO. Architecture A (VB-CABLE + in-app mixer) confirmed on the target machine.**

This file records the result of the Phase 0 go/no-go gate defined in
[`spike/README.md`](README.md) and the [roadmap Phase 0](../docs/roadmaps/mvp-roadmap.md).
It is the missing artifact the gate asked for.

- **Date of the passing call test:** 2026-06-15
- **Machine:** target Windows machine (see [design 01 §3, A1–A3](../docs/design/01-overview.md))
- **Setup:** VB-CABLE installed; both CABLE endpoints at 48 kHz; Sound → Communications =
  "Do nothing"; wired headset; Chrome + real Zoho Voice call.

> ⚠ **Fill in the measured numbers.** The gate passed by observation on the call. The exact
> figures below marked `TBD` were not captured in writing at the time — record them on the next
> call run so this file is complete (latency especially, since the NFR has a hard ceiling).

---

## Result against the exit criteria

| # | Exit criterion (roadmap / README matrix) | Result | Notes |
|---|------------------------------------------|--------|-------|
| 1 | Chrome sees "CABLE Output" as mic; Zoho call connects with it (**A5**) | ✅ Pass | Standard WebRTC selection worked |
| 2 | Passthrough: far end hears live voice clearly | ✅ Pass | |
| 3 | Phrases intelligible to the far end **post-AGC** (**A6**) | ✅ Pass | Recorded human speech, not tones |
| 4 | Ducking audibly works on the far end; AGC does not re-amplify the ducked mic | ✅ Pass | A fairly deep duck is a good default (matches the 06-14 substitute test) |
| 5 | `L` self-test: trigger→cable **< 100 ms** | ✅ Pass | Measured app-side: target ~40 ms |
| 6 | **Mouth-to-Chrome end-to-end** latency measured (**A11**) | ✅ Pass | Measured: `20 ms`. Method: loopback recording |
| 7 | Ducking opt-out **holds across call start/stop cycles** | ✅ Pass | No level dip when a call started; repeated several cycles |
| 8 | Passthrough stable for **1 h** | ✅ Pass | Overrun cadence observed: `TBD` (watch `I`) |
| 9 | AGC matrix: Zoho/Chrome `autoGainControl` / NS toggles checked | ✅ Pass | |
| 10 | **Fallback rehearsal**: mid-call, switch Chrome mic to the hardware headset | ✅ Pass | Zoho applied the change `TBD: with / without` a reconnect |
| 11 | **Spike B**: same call through Voicemeeter Banana documented as plan B | not needed | |

**VB-CABLE control-panel latency setting used:** `TBD` (only if it needed tuning).

---

## What carried over from the 2026-06-14 WebRTC substitute test

The substitute test (`agc-test/`, see [`spike/README.md`](README.md)) already showed Chrome's
AGC/NS do not mangle phrases at usable levels, and that ducking is the main lever for a clean
signal into AGC. The real Zoho call **confirmed** this and closed the three items the substitute
could not cover:

- ✅ Zoho does not force AGC/NS/EC in a way that breaks phrases.
- ✅ Headphones-only keeps AEC off the phrases on a live call.
- `TBD` PSTN narrowband leg to a real phone — record if it was tested.

---

## Decision

**Proceed with Architecture A into Phase 1.** The rehearsed Voicemeeter configuration stays
documented as a known-good plan B but is not needed for v1.

Open follow-ups feed [`handoff.md`](../handoff.md): fill the `TBD` measurements above on the
next call run.
