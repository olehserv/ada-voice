# Phase 0 Spike — Architecture A (VB-CABLE + in-app mixer)

> **Status: Phase 0 gate PASSED (GO), 2026-06-15** — results in
> [PHASE0-RESULTS.md](PHASE0-RESULTS.md). The test matrix below is the historical
> checklist; its boxes were never ticked.

**Throwaway console prototype** per [the MVP roadmap](../docs/roadmaps/mvp-roadmap.md).
Not production code; nothing here ships. The one reusable artifact is
`DuckingOptOut.cs` — the `IAudioSessionControl2::SetDuckingPreference` COM shim
that NAudio doesn't wrap (reference implementation for Phase 1).

## Permission gate — resolved

**A8 — no employer/Zoho agreement required (employer is loyal), confirmed 2026-06-13.**
No longer a gate (design 01 §4, decision #20). This spike still has to answer the
*technical* unknowns A5/A6 — does Zoho/Chrome pass pre-recorded speech through AGC
intelligibly — see the test matrix below.

## Prerequisites (target machine)

1. **VB-CABLE** — https://vb-audio.com/Cable/ (donationware; install manually,
   never redistribute). Reboot after install.
2. Sound settings: set **both** "CABLE Input" (playback) and "CABLE Output"
   (recording) to **48000 Hz** (Properties → Advanced).
3. Sound → Communications → **"Do nothing"** (belt-and-braces for ducking).
4. Default output must NOT be CABLE Input (or you won't hear anything).
5. .NET 10 SDK (already on this machine: 10.0.202).

## Run

```powershell
cd C:\p\ada-voice\spike\AdaVoice.Spike
dotnet run -- --list                  # enumerate devices
dotnet run                            # default comms mic -> CABLE Input
dotnet run -- --mic "USB Audio"       # pick mic by name substring
dotnet run -- --phrases C:\path\wavs  # real recorded phrases (recommended)
```

From WSL: `"/mnt/c/Program Files/dotnet/dotnet.exe" run` in the project dir
(audio still runs on the Windows side).

Keys: `1-9` play phrase · `S` stop (10 ms fade) · `D` duck on/off ·
`+/-` duck dB · `L` trigger→cable latency self-test · `I` buffer stats · `Q` quit.

With no `--phrases`, three synthetic WAVs are generated (tone, speech-band
sweep, AM bursts). **The real AGC test needs recorded human phrases** — record
a few sentences in Audacity (48 kHz mono WAV) and pass the folder.

## Phase 0 test matrix (roadmap exit criteria)

- [x] **A8**: employer/Zoho permission — resolved 2026-06-13 (no agreement needed; employer loyal)
- [ ] Chrome sees "CABLE Output" as mic; Zoho call connects with it
- [ ] Passthrough: far end hears live voice clearly through the spike
- [ ] Phrases intelligible to the far end **post-AGC** (use recorded speech, not test tones)
- [ ] Ducking audibly works on the far end; check whether Chrome AGC re-amplifies the ducked mic
- [ ] `L` self-test: trigger→cable **< 100 ms** (tune VB-CABLE control-panel latency if over)
- [ ] **Mouth-to-Chrome end-to-end**: record what Chrome receives (e.g. a second
      machine on the call, or chrome://webrtc-internals dumps) while clapping; measure offset
- [ ] Ducking opt-out **holds across call start/stop cycles** (start/stop several Zoho calls;
      phrase/passthrough level must not dip when a call starts)
- [ ] Passthrough stable for **1 h** (watch `I` for overrun cadence; log counts)
- [ ] AGC matrix: check Zoho/Chrome for `autoGainControl` / noise-suppression toggles; document
- [ ] **Fallback rehearsal**: mid-call, switch Chrome's mic to the hardware headset —
      does Zoho apply it without reconnecting?
- [ ] **Spike B (~half a day)**: same call through Voicemeeter Banana; document the
      working config as known-good plan B

**Go/no-go**: all of the above pass → Phase 1. A fails → adopt the rehearsed
Voicemeeter config and re-scope Phase 1 (engine shrinks to a soundboard).

## Phase 0 findings — WebRTC substitute test (2026-06-14)

No Zoho access yet, so Chrome's mic-processing risk (A5/A6) was tested against a
**WebRTC substitute**: the local helper page `agc-test/` capturing **CABLE Output**
through `getUserMedia`, on **headphones**, while the spike fed mic + phrases into
**CABLE Input**. This exercises the same Chrome AGC/NS/EC pipeline a softphone uses;
it does **not** cover Zoho's own constraints or the PSTN leg.

### Results

| Chrome setting | Observed effect on the phrase | Verdict |
|----------------|-------------------------------|---------|
| **autoGainControl** | Off → clean, room noise barely present during a phrase. On → overall level higher/sharper, room noise up slightly, **phrase still plays well**. | Mild, survivable. |
| **noiseSuppression** | No noticeable impact. | Neutral. |
| **echoCancellation** | On → phrases played badly, choppy, dropped when room noise occurred. **Traced to a test artifact**, not real behaviour (see below). | Harmless on a real headset call; **highest-risk item to confirm on Zoho**. |
| **App ducking (spike)** | Works as designed. Deeper duck (more-negative dB) → less room noise during a phrase; shallower → more. | ✅ Confirmed. |

### The echoCancellation artifact (important)

AEC removes from the mic whatever matches the **speaker** output. The helper page
*monitors the captured cable back to the headphones*, so AEC saw the phrase coming
out the speaker **and** in the mic → treated the phrase as echo → cancelled/chopped
it. Turning the page's **"monitor to headphones" off** broke that loop and the level
held steady. On a real call the speaker plays the **far-end voice** (uncorrelated
with the phrase), so AEC should leave phrases alone — *provided a headset is used*.

### What this confirms

- Chrome's AGC/NS do **not** mangle phrases at usable levels.
- **Ducking is the main lever for a clean signal into AGC**: a ducked mic during a
  phrase means less room noise on the cable, so AGC has little to react to.
- A **fairly deep duck** is a good default here (phrases are the operator's voice, so
  she is usually not talking *during* one). Final value → operator pilot.
- The **wired-headset decision** (design 01 §4) gains a second justification: it keeps
  AEC away from the phrases.

### Still open — needs a real Zoho call

- [ ] Does **Zoho force** AGC / NS / **EC** on with no user toggle?
- [ ] Confirm **headphones-only** keeps AEC off the phrases on a live call.
- [ ] **PSTN narrowband** leg to a real phone (substitute stays wideband Opus).
