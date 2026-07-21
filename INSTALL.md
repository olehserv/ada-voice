# Installing AdaVoice (Beta)

*Українською: [INSTALL.uk.md](INSTALL.uk.md)*

This is a beta build for trying out AdaVoice and giving feedback. See [handoff.md](handoff.md)
for what's finished vs. still being built.

There are two parts. **Do Part 1 first, on your own** — it's quick and needs nothing extra.
**Do Part 2 together** (a screen-share works best) — it needs a separate driver install and
a couple of Windows settings.

## Part 1 — Install and try the app

1. **Download** the installer you were sent (`AdaVoice-Setup-*.exe`) and run it.
2. Windows will likely show a blue **"Windows protected your PC"** warning. This is normal —
   it just means the app isn't digitally signed yet (that costs money and comes later).
   Click **"More info"**, then **"Run anyway"**. You only need to do this once.
3. Click through the installer (**Next → Next → Install → Finish**). It installs just for
   your account — no admin password needed. Leave "Launch AdaVoice" checked on the last
   page, or find it later in the Start Menu.
4. The app opens straight into a short **setup wizard**. A few checks will fail (that's
   expected — see Part 2) — just click **"Skip anyway"** to move past them, and click
   through to the end.
5. You're on the main board. **This is what to try and give feedback on:**
   - Record a phrase or two (your own voice) and play them back with headphones.
   - Organize phrases into categories.
   - Try a "Conversation" (an ordered set of phrases with a step-by-step guide).
   - General feel: is anything confusing, ugly, slow, or unclear?

Nothing you record leaves your machine — everything is stored locally, no internet needed.

**Playing a phrase into a real call doesn't work yet at this point** — that's Part 2.

*To uninstall later: Windows Settings → Apps → AdaVoice → Uninstall (same as any app).*

## Part 2 — Enable playing phrases into a real call

Windows has no built-in way for an app to "speak into" a microphone that another app (like
Chrome) can pick up. AdaVoice uses a free, well-known helper driver called **VB-CABLE** to
do that — it's the same idea as a physical audio cable, just virtual. It's a separate,
one-time install.

1. Download VB-CABLE from **https://vb-audio.com/Cable/** (there's also a link inside the
   app's setup wizard, on the "Environment checks" step).
2. Unzip it, then right-click **`VBCABLE_Setup_x64.exe`** → **"Run as administrator"** →
   **"Install Driver"**.
3. **Restart your computer.** (Required — the driver won't be picked up otherwise.)
4. Open Windows **Sound settings**. Find **CABLE Input** and **CABLE Output** — for each one,
   open its properties/advanced tab and set the format to **24 bit, 48000 Hz** (a.k.a.
   "48000 Hz"). This step is easy to miss and is the most common reason things don't work
   afterward — please don't skip it.
5. In your call app (Chrome, for your Zoho calls), open the call's microphone setting and
   choose **"CABLE Output (VB-Audio Virtual Cable)"** instead of your real microphone.
6. Back in AdaVoice, open **Setup…** again and click **"Re-check"**. All the checks should
   now pass (green).
7. Do one real test: start or join a call (calling your own phone is a good test), play a
   phrase from AdaVoice, and confirm the other side hears it clearly.

## Switching the app's language

AdaVoice also speaks Ukrainian and Polish. To switch:

1. Open **Settings** (the gear icon).
2. Under **"Language & Backup"**, pick **Language** → your language.
3. The app will offer to restart — accept it. The change takes effect after restart.

## If something doesn't work

- **The app won't open / closes immediately:** tell us what you saw — a screenshot of any
  error is the most useful thing you can send.
- **A check still fails after Part 2:** the 48 kHz setting (step 4) is the most common miss —
  double check both CABLE Input *and* CABLE Output.
- **No sound in the call at all:** confirm the call app's microphone is set to "CABLE Output",
  not your real mic.

## Sending feedback

Anything goes — confusing screens, things you expected to work differently, missing
features, or "this is great, don't change it." Just tell us directly (call, message, or
however's easiest) — this beta isn't tracking anything automatically.
