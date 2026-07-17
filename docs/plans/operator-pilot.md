# AdaVoice — Operator Pilot Plan (Phase 3 gate)

> **Outcome: PASSED 2026-06-29.** The supervised pilot ran on the real machine; the operator
> "tested everything, works awesome" (see handoff). The script below is kept for re-runs —
> for example before the next big UI change or the monetization pilot.

**What this is:** the script for the half-day supervised pilot with the real operator, on the
real machine. It is the **only acceptance gate before the late phases**.

**Goal for THIS pilot (chosen 2026-06-29):** *functional smoke — does it work at all, safely,
on the real machine in a real test call?* The operator **records fresh phrases live** during
the session. UX feedback is secondary: collect a few open questions, do **not** run a full
ergonomics battery yet.

**Hard rule:** **test calls only, never a real client call.** You (the developer) sit next to
the operator the whole time.

---

## 0. Before the operator arrives — dev dry run (do not skip)

Phase 0 proved the **console host** into a real call. The **WPF app's** full
mic → cable → Chrome path may never have run live end to end (the handoff only confirms it
"renders and plays a take"). So **run the entire session script below yourself first**, on a
test call. Catch first-run breakage on your own time, not the operator's.

If the dry run fails, fix it (or shrink the pilot scope) before the operator comes.

## 1. Pre-flight machine setup

Reuse the setup from [handoff.md → Next action](../../handoff.md). Confirm all of:

- [ ] VB-CABLE installed; **`CABLE` set to 48 kHz** (or `Start` stays Stopped, by design)
- [ ] Windows **default playback ≠ `CABLE Input`** (preview refuses if it is — cardinal rule)
- [ ] Chrome / Zoho **microphone = `CABLE Output`**
- [ ] A working **headset** for the operator (this is also the fallback device — see §4)
- [ ] App builds and launches: `dotnet run --project src/AdaVoice.App`
- [ ] A second person / phone ready to be the **far end** on a Zoho **test** call

**Not testable in the 2026-06-29 pilot:** the global `Pause` stop hotkey was not built yet.
*(It shipped 2026-07-01 — include it in any re-run.)*

## 2. Session script (ordered)

Run top to bottom. Each step has a clear pass check.

| # | Step | Action | Pass check |
|---|------|--------|-----------|
| 1 | Go Live | Press **Start** | State line shows **Live**; phrase buttons enable |
| 2 | Passthrough | Operator talks on the test call | Far end hears the live voice clearly |
| 3 | Record fresh | **Record → speak a phrase → Stop record** | State drops to **OFF AIR** while recording; **far end hears silence** (no fumbling) ✅ |
| 4 | Preview | **Preview** the take | Operator hears the take on the headset (not the call) |
| 5 | Save | Name it → **Save** | Phrase appears on the board immediately |
| 6 | Repeat | Record **3–5 phrases** total | Each saves and shows; operator finds the flow doable |
| 7 | Play to call | Press a phrase button mid-call | Far end hears the phrase clearly; button **glows** while playing |
| 8 | Live between phrases | Talk right after a phrase | No level jump; live voice returns smoothly |
| 9 | STOP | Press the big **STOP** | Phrase stops within a blink |
| 10 | OFF AIR | Toggle **OFF AIR** on/off | Far end hears silence when OFF AIR; live voice returns on exit |
| 11 | Stop engine | Press **Stop** | Returns to Stopped cleanly; no crash |

## 3. Hardware / audio-quality checks

These can only be verified on real hardware. **Do not duplicate them here** — run the relevant
items from the canonical list:
[design 08 §4 — Manual call-test checklist](../design/08-testing.md#4-manual-call-test-checklist-only-hardware-can-verify-these).
Skip the `Pause`-hotkey and 8-hour-soak rows for this short pilot.

## 4. Safety / abort line

If anything sounds wrong to the far end, in this order:

1. Press the on-screen **STOP** (stops the phrase).
2. If needed, press **OFF AIR** or **Stop** the engine.
3. Last resort: **switch Chrome's mic back to the hardware headset** mid-call — the call keeps
   going. *(This doubles as the 08 §4 fallback-rehearsal item — tick it if you use it.)*

## 5. Findings capture (fill in during the session)

Keep it short. One line per item.

**Blockers (must fix before Phase 4):**
-

**Bugs / surprises:**
-

**Operator UX — a few open questions only:**
- Are the phrase buttons big enough / easy to hit?
- Is it obvious which phrase is playing (the glow)?
- Is OFF AIR clear enough?
- Does the record → name → save flow feel simple?
- Duck level: is the live voice under a phrase about right?

**Did it work at all? (the gate):** ☐ yes  ☐ no — note why:

## 6. After the pilot

- Write the findings into [handoff.md](../../handoff.md) (a new dated entry).
- Blockers → fix before starting Phase 4.
- UX notes → feed into the deferred Phase 3 UI/UX polish.
- If the gate passed, the project is cleared to move toward **Phase 4** (stop hotkey,
  settings, finish the setup wizard).
