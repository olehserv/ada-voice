# 07 — Error Handling, Security & Privacy, Risks

## 1. Error handling & edge cases

| Case | Behavior |
|---|---|
| VB-CABLE not installed / removed | Engine STOPPED; board disabled with clear message; setup wizard re-opens |
| Mic unplugged mid-call | Auto-retry rebuild with backoff; DEGRADED banner + alarm tone in headphones (she must know the client cannot hear her) |
| Device renumbering (Windows update, USB re-enumeration) | Devices stored by MMDevice ID with friendly-name fallback; prompt if ambiguous — never guess |
| App crash | Single-instance relaunch restores engine + "engine recovered" toast; heartbeat file lets restart detect unclean exit and log it |
| Phrase file missing / corrupt | Button shows broken state; playback refuses gracefully; startup library validation |
| Two AdaVoice instances | Single-instance mutex; second launch focuses the first window |
| Disk full while recording | Pre-check free space; writer failure aborts the take with a message; temp file cleaned |
| Device with mismatched sample rate (e.g., 44.1 kHz cable) | Engine resamples; logged |
| Low-quality capture format detected (8/16 kHz) | Explicit warning (legacy guard; wired headset confirmed, so unlikely) |
| Render callback stalls | Watchdog forces stream rebuild after 500 ms of silence on the pull side |

## 2. Security & privacy

- **Fully local:** no network calls, no telemetry, no accounts. All audio and metadata stay
  on the machine under `%LOCALAPPDATA%\AdaVoice\` (user profile ACLs apply).
- Recordings are the operator's **own voice** — still personal data. Backups/exports are
  **unencrypted zips** in v1; this is documented to the user. Encrypted export is a planned
  enhancement.
- The app never captures the client's side of a conversation — only the operator's
  microphone, and only while she records phrases or runs passthrough live.

## 3. Legal / ethical / product safety

- **Transparency & consent:** AdaVoice plays *pre-recorded phrases of the real operator, who
  is present and driving the conversation*. It is an efficiency aid — not call automation,
  not voice impersonation.
- **No deception by design:** the product deliberately excludes features that simulate
  presence — no auto-replies, no scheduled playback, no unattended operation. One trigger =
  one human decision.
- **Workplace/platform compliance (open item):** the operator should confirm her employer's
  and Zoho's terms permit assistive audio tools; some platforms prohibit non-live audio.
  Recommendation: disclose the tool to the employer. *(Assumption A8 — unverified.)*
- If the platform records calls, AdaVoice adds no new client data — phrase audio is already
  the operator's own.

## 4. Risk register

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| App is single point of failure for her live mic (passthrough dies → client hears silence) | High | Medium | Watchdog + auto-rebuild; loud DEGRADED alarm; documented 60-second fallback (switch Chrome mic to hardware); Voicemeeter architecture as plan B |
| Chrome/Zoho noise suppression degrades phrase audio | Medium | Medium | **Phase 0 spike against a real Zoho Voice call** (go/no-go gate); clean −3 dBFS recordings; disable optional NS toggles |
| VB-CABLE install friction (driver, admin, AV warnings) | Medium | Medium | Wizard with screenshots and verify-after-install; admin access confirmed available |
| Echo/feedback (monitor leaking into mic) | Medium | Low | Wired headset confirmed; monitoring goes to headphones only |
| Latency creep over long sessions (buffer drift) | Medium | Medium | Bounded buffer with drop-oldest + logging; 8-hour soak test in Phase 1 |
| Stop hotkey collides with another app | Low | Low | Conflict detection at registration; reassignable |
| Employer/platform policy forbids the tool | High | Unknown | Disclosure recommendation; user to verify (A8) |
| WASAPI edge cases (exclusive-mode apps stealing devices) | Medium | Low | Shared mode everywhere; rebuild logic |
| NAudio/driver quirks specific to her hardware | Medium | Unknown | Phase 0 spike runs on the actual target machine |

## 5. Fallback playbook (for the user guide)

1. **Phrase audio sounds bad to clients** → lower phrase gain; check Zoho NS settings;
   if persistent, switch to Voicemeeter architecture.
2. **Mic dead mid-call (DEGRADED alarm)** → keep talking is pointless; in Chrome/Zoho switch
   microphone back to the hardware headset (≈ 30–60 s), finish the call, restart AdaVoice.
3. **App won't start / engine won't go LIVE** → run setup wizard self-test; verify CABLE
   devices exist in Windows Sound settings; reinstall VB-CABLE if missing.
