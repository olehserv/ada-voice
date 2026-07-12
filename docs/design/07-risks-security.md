# 07 — Error Handling, Security & Privacy, Risks

## 1. Error handling & edge cases

| Case | Behavior |
|---|---|
| VB-CABLE not installed / removed | Engine STOPPED; board disabled with clear message; setup wizard re-opens |
| Mic unplugged mid-call | Auto-retry rebuild with backoff; DEGRADED banner + alarm tone on the **system default device** (she must know the client cannot hear her) |
| Windows communications ducking attenuates the cable stream when a call starts | Engine opts out per session (`SetDuckingPreference`); wizard sets Sound → Communications → "Do nothing" as fallback (decision #12) |
| AdaVoice session muted in Volume Mixer | Silent total failure otherwise — a wizard check that reads the session mute state is *planned — not built yet* (shipped wizard runs the 4-check subset, see 05 §4) |
| Default output device is CABLE Input (common post-install state) | Every system sound would play to the client — wizard checks the default output device and warns (*shipped*; communications-device check *planned*) |
| Recording attempted while a call could be active | Not possible to leak: Recorder open ⇒ OFF AIR (cable paused, banner) by design (decision #11) |
| Device renumbering (Windows update, USB re-enumeration) | Devices stored by MMDevice ID with friendly-name fallback; prompt if ambiguous — never guess |
| App crash | `RegisterApplicationRestart` relaunches; auto-restore engine + "engine recovered" toast. A heartbeat file for unclean-exit detection is *planned — not built yet* |
| Windows mic privacy settings block desktop apps | Wizard pre-check with fix-it link (Settings → Privacy → Microphone) — *planned — not built yet* |
| Phrase file missing / corrupt | Button shows broken state; playback refuses gracefully; startup library validation |
| Two AdaVoice instances | Single-instance mutex; second launch shows an "AdaVoice is already running" message and exits |
| Disk full while recording | Pre-check free space; writer failure aborts the take with a message; temp file cleaned |
| VB-CABLE at 44.1 kHz (its common default) | Wizard verifies shared-mode format = 48 kHz and offers to fix — avoids permanent resampling on the hot path |
| Device with mismatched sample rate at runtime | Engine resamples; logged |
| Low-quality capture format detected (8/16 kHz) | Explicit warning (legacy guard; wired headset confirmed, so unlikely) |
| Render callback stalls | Watchdog forces stream rebuild after 500 ms of silence on the pull side |
| Clock drift overrun / underrun | Drop-oldest / insert-silence respectively, both logged; recurring events are a Phase 1 fix, not accepted behavior (06 §1) |

## 2. Security & privacy

- **Fully local:** no network calls, no telemetry, no accounts. All audio and metadata stay
  on the machine under `%LOCALAPPDATA%\AdaVoice\` (user profile ACLs apply).
- Recordings are the operator's **own voice** — still personal data. Backups/exports are
  **unencrypted zips** in v1; this is documented to the user. Encrypted export is a planned
  enhancement.
- Deleted phrases remain on disk as orphaned files (decision #14) — documented to the user;
  true removal means deleting the file manually or via a future "purge orphans" tool.
- The app never captures the client's side of a conversation — only the operator's
  microphone, and only while she records phrases or runs passthrough live.
- **Path-traversal via `library.json` — fully closed (2026-07-12).** The 2026-07-11 fix flattened
  every phrase/version `FileName` on load; the follow-up flattens the version WAV name built from a
  phrase `Id` too (`PhraseLibraryService.AddPhraseVersion`), so a crafted `Id` can no longer write a
  recorded version outside `audio\`. See
  [reviews/2026-07-12-security-scan.md](../reviews/2026-07-12-security-scan.md) finding 1.
- The installer is **not code-signed in v1** (decision #19): SmartScreen will show an
  "unrecognized app" warning on first install. Accepted for personal/family use; the user
  guide explains the warning. Revisit (EV/OV certificate) if the app is ever distributed.

## 3. Legal / ethical / product safety

- **Transparency & consent:** AdaVoice plays *pre-recorded phrases of the real operator, who
  is present and driving the conversation*. It is an efficiency aid — not call automation,
  not voice impersonation.
- **No deception by design:** the product deliberately excludes features that simulate
  presence — no auto-replies, no scheduled playback, no unattended operation. One trigger =
  one human decision.
- **Workplace/platform compliance — resolved (2026-06-13, decision #20):** no employer or
  Zoho-terms agreement is required (employer is loyal). No longer a gate. The product's
  transparency/consent stance above (real operator present, one trigger = one human decision)
  is what keeps it appropriate to use.
- If the platform records calls, AdaVoice adds no new client data — phrase audio is already
  the operator's own.

## 4. Risk register

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| App is single point of failure for her live mic (passthrough dies → client hears silence) | High | Medium | Watchdog + auto-rebuild; `RegisterApplicationRestart`; alarm on system default device; **fallback was rehearsed in Phase 0** (mid-call Chrome mic switch tested on a live call, gate passed 2026-06-15); Voicemeeter spike made plan B known-good |
| Chrome/Zoho processing degrades phrase audio (NS, EC, **AGC**) | Medium | Medium | **Phase 0 spike ran on a real Zoho Voice call with AGC in the test matrix** — go/no-go gate **passed 2026-06-15**; loudness-matched recordings; disable NS/AGC toggles if exposed |
| AGC counteracts ducking / re-levels phrases (perceived levels ≠ configured levels) | Medium | Medium | Duck and gain defaults were tuned against post-AGC output in Phase 0; calibration step gives a consistent baseline |
| Loudness mismatch phrases vs live voice breaks the "as if spoken" illusion | Medium | High (with peak-only normalization) | RMS loudness matching to the wizard-calibrated mic reference (decision #13) |
| End-to-end latency exceeds targets (VB-CABLE internal buffer + Chrome buffering not in app budget) | Medium | Medium | Phase 0 measured **mouth-to-Chrome** and the gate passed 2026-06-15 (A11; exact numbers not recorded) |
| VB-CABLE install friction (driver, admin, AV warnings, default-device hijack) | Medium | Medium | Wizard verify-after-install; default-device + format checks *shipped*; privacy + mixer checks and screenshots *planned*; admin access confirmed available |
| Echo/feedback (monitor leaking into mic) | Medium | Low | Wired headset confirmed; monitoring goes to headphones only |
| Latency creep over long sessions (buffer drift) | Medium | Medium | Bounded buffer with drop-oldest/insert-silence + logging; 8-hour soak test in Phase 1 |
| Stop hotkey unusable (missing Pause key) or conflicting | Low | Low | Wizard existence check + live press-to-test; `Ctrl+F12` fallback; conflict detection at registration |
| Employer/platform policy forbids the tool | High | **Resolved (2026-06-13)** | No employer/Zoho agreement needed (employer is loyal); risk closed (decision #20) |
| Operator rejects the UX (buttons, workflow, focus) | Medium | **Resolved** | Supervised operator pilot after Phase 3 (decision #20) — **passed 2026-06-29** |
| WASAPI edge cases (exclusive-mode apps stealing devices) | Medium | Low | Shared mode everywhere; rebuild logic |
| NAudio/driver quirks specific to her hardware | Medium | Low (post-spike) | Phase 0 spike ran on the actual target machine (passed 2026-06-15) |

## 5. Fallback playbook (for the user guide)

1. **Phrase audio sounds bad to clients** → re-run calibration; check Zoho/Chrome NS+AGC
   settings; if persistent, switch to the rehearsed Voicemeeter configuration.
2. **Mic dead mid-call (DEGRADED alarm)** → in Chrome/Zoho switch microphone back to the
   hardware headset (≈ 30–60 s; **this exact move was rehearsed in Phase 0 on a live
   call**), finish the call, restart AdaVoice (or let Windows relaunch it after a crash).
3. **App won't start / engine won't go LIVE** → run setup wizard self-test; verify CABLE
   devices exist in Windows Sound settings; reinstall VB-CABLE if missing.
4. **Windows says "Windows protected your PC" at install** → expected (unsigned installer,
   decision #19): More info → Run anyway. Documented with a screenshot in the user guide.
