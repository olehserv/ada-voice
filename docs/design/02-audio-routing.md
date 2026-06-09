# 02 — Audio Routing to the Microphone (Core Technical Challenge)

## 1. Why this is hard

Windows audio (WASAPI / MMDevice API) strictly separates **render** endpoints (speakers,
headphones) from **capture** endpoints (microphones). Applications can play to render
endpoints and read from capture endpoints — **there is no supported user-mode API to write
samples into a capture endpoint.**

Consequence, stated honestly: **pure .NET / NAudio / WASAPI alone cannot make Chrome "hear"
our audio as a microphone.** The only way another application can receive our audio as mic
input is through a **driver** that exposes a virtual capture endpoint mirroring a playback
endpoint. This is a hard platform limitation, not a library gap.

A second, subtler problem is **mixing**: Chrome can select only *one* microphone. If Chrome's
mic is the virtual cable, the operator's real voice no longer reaches the call unless
something continuously forwards the hardware mic into that same cable. That forwarding and
mixing is the heart of AdaVoice's audio engine.

## 2. Options analysis

| # | Approach | How it works | Verdict |
|---|----------|--------------|---------|
| A | **VB-CABLE virtual cable** | Driver exposes a render endpoint ("CABLE Input") whose audio appears on a paired capture endpoint ("CABLE Output"). App plays into the render side; Chrome uses the capture side as its mic. | ✅ **Recommended.** Free (donationware), ubiquitous, stable, built exactly for this. |
| B | Voicemeeter (Banana) | Full virtual mixer: hardware mic + app audio mixed inside Voicemeeter; Chrome uses "Voicemeeter Out" as mic. AdaVoice would only play phrases to a Voicemeeter input. | Solid fallback — **rehearsed in Phase 0** (~half a day) so switching is a known-good move, not a theory. Mixing happens outside our app (robust, removes the single point of failure in §5), but the operator must run and configure a second, complex application. |
| C | Windows "Listen to this device" | Control Panel feature routes hardware mic → CABLE Input with no code. | Zero-code passthrough fallback, but adds 50–150 ms latency and lives in hidden, fragile OS UI. Documented as plan B only. |
| D | Write our own virtual audio driver | AVStream/ACX kernel driver or APO, WHQL-signed. | ❌ Rejected. Months of work, signing cost and process, kernel risk. Not realistic for a solo developer. |
| E | Hook the mic stream of other apps (Soundpad-style) | Code injection into other processes' audio sessions. | ❌ Rejected. Fragile, antivirus flags, broken by browser sandboxing. |

## 3. Recommended architecture (Option A + in-app mixer)

```mermaid
flowchart LR
    subgraph HW["Hardware (wired headset)"]
        MIC["Microphone"]
        HP["Headphones"]
    end

    subgraph APP["AdaVoice audio engine"]
        CAP["WASAPI capture<br/>shared, event-driven, 20 ms"]
        DUCK["Mic gain stage<br/>micDuckDb (live-configurable)"]
        CACHE["Phrase cache<br/>RAM, 48 kHz float"]
        MIX["MixingSampleProvider"]
        MON["Monitor gain stage<br/>monitorPhraseDb (live-configurable)"]
    end

    subgraph VBC["VB-CABLE driver"]
        CIN["CABLE Input<br/>(render endpoint)"]
        COUT["CABLE Output<br/>(capture endpoint)"]
    end

    subgraph CHR["Google Chrome"]
        ZV["Zoho CRM / Zoho Voice<br/>softphone (WebRTC)"]
    end

    MIC --> CAP --> DUCK --> MIX
    CACHE -->|"phrase trigger"| MIX
    MIX -->|"WASAPI render"| CIN
    CIN -.->|"driver-internal pair"| COUT
    COUT -->|"selected as Chrome microphone"| ZV
    CACHE --> MON --> HP
    ZV -->|"client's voice — normal path, untouched"| HP
```

Key properties:

- Chrome's **speaker** setting stays on her headphones — the client's voice path is untouched.
- The app holds **one persistent render stream** to CABLE Input; phrases are mixed in and out
  without reopening devices → instant start/stop, no device-open latency on the hot path.
- Mic ducking (`micDuckDb`, default −12 dB, range −60…0 dB with mute floor, 50 ms ramp) and
  phrase monitor level (`monitorPhraseDb`, default −6 dB) are independent live-adjustable
  gain stages.
- The engine **opts out of Windows communications ducking** on its cable and monitor
  sessions (`IAudioSessionControl2::SetDuckingPreference`, small COM interop — NAudio does
  not wrap it). Without this, Windows attenuates AdaVoice's output the moment Chrome opens
  a communications stream — i.e., exactly when a call starts. The wizard additionally sets
  Sound → Communications → "Do nothing" as a belt-and-braces fallback.

## 4. Browser / Zoho caveats

- Chrome processes microphone input at the getUserMedia layer: **echo cancellation, noise
  suppression, and automatic gain control (AGC)**, with resampling to roughly 32 kHz mono on
  desktop. Zoho Voice may add its own processing on top.
- **AGC is the most adversarial component for this design**: it can re-amplify the ducked
  mic (nullifying the configured duck level), pump between phrase and live voice, and
  re-level phrase loudness. The duck/gain values the operator tunes may not match what the
  client hears. Phase 0 tests with AGC explicitly in the matrix; if Zoho/Chrome expose an
  `autoGainControl` toggle, the wizard documents it.
- Played phrases are normal human speech, so they are expected to pass intelligibly, but
  double processing can color the audio. **Assumption A6 — unverified until the Phase 0
  spike runs a real Zoho call.**
- Mitigations: record clean phrases loudness-matched to her live mic level (see calibration,
  decision #13); disable optional noise-suppression/AGC toggles in Zoho/Chrome if available.

## 5. Honest limitation: single point of failure

With Architecture A, **AdaVoice is the component forwarding her live voice.** If the app
crashes or its render stream dies, her microphone goes silent in the call.

Mitigations (details in [07-risks-security.md](07-risks-security.md)):

1. Engine watchdog with automatic stream rebuild on device errors.
2. Loud DEGRADED state: visual banner + alarm tone played on the **system default output
   device** (not the optional monitor stream — the alarm must survive `monitorEnabled=false`).
3. Process-death resilience: the app calls `RegisterApplicationRestart` so Windows relaunches
   it after a crash; on unclean-exit restart the engine auto-restores and shows a recovery toast.
4. Documented 60-second manual fallback: switch Chrome's mic back to the hardware device.
   **Rehearsed during Phase 0 on a live test call** — whether Zoho applies a mid-call mic
   change without reconnecting must be verified, not assumed.
5. Documented OS-level fallback: Windows "Listen to this device" (Option C).
6. Voicemeeter (Option B) is spiked in Phase 0 alongside Architecture A, so if in-app
   passthrough proves unreliable, the switch is to a known-good configuration — AdaVoice then
   becomes a plain soundboard and the mixing moves into Voicemeeter's engine.

## 6. VB-CABLE licensing note

VB-CABLE is donationware and **may not be silently bundled or redistributed** with the
installer. The setup wizard links to the official download, lets the user run the install,
and verifies the device afterwards.
