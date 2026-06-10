# Phase 0 Spike — Architecture A (VB-CABLE + in-app mixer)

**Throwaway console prototype** per [the MVP roadmap](../docs/roadmaps/mvp-roadmap.md).
Not production code; nothing here ships. The one reusable artifact is
`DuckingOptOut.cs` — the `IAudioSessionControl2::SetDuckingPreference` COM shim
that NAudio doesn't wrap (reference implementation for Phase 1).

## Gate first (do NOT skip)

**A8 — employer/Zoho permission confirmed by email before building further.**
A "no" kills the project (design 01 §4, decision #20).

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

- [ ] **A8 gate**: employer/Zoho permission email confirmed
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
