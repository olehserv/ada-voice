# ADR-0004: Use Offline Grace Period

## Status

Proposed

## Context

AdaVoice operators play recorded phrases into live calls. The worst possible failure is a
licensing block in the middle of a working day because the internet dropped or our server
was briefly down. A network blip must never stop a paying customer's calls.

License tickets are short-lived (24 h, ADR-0002). Without extra design, a client that cannot
refresh would lose entitlement one day after its last successful refresh. That is far too
strict for real offices with flaky networks, weekends, and holidays.

We need a defined window in which the app keeps working fully offline, and a clear rule for
what happens when that window ends.

## Decision

Every license ticket carries a **`graceUntil`** timestamp: the time until which offline use
is allowed.

- Paid subscription: `graceUntil` = issue time + **7 days**.
- Trial: issue time + **2 days** (configurable 1–3 per plan).
- The server never sets `graceUntil` past the subscription's own hard end (for example a
  suspension date).

Client behavior: refresh silently on startup and after >50% of the ticket TTL (~every 12 h).
If refresh fails, keep full functionality from the cached ticket while `now <= graceUntil`
(UX state `offline_allowed`). After `graceUntil`, the state becomes `offline_blocked`:
premium features stop (phrase playback into calls), the app shows a clear full-window
message with a Retry/Reconnect action, and local user data is never deleted.

The ClockGuard protects the window: if the local clock rolls back more than 5 minutes past
`lastAcceptedUtc`, the client also enters `offline_blocked` until an online refresh.

## Consequences

Pros:

- A week of full offline operation for paying customers; server maintenance and network
  outages never interrupt calls.
- Simple mental model for support: "paid = 7 days offline, trial = 2 days".
- The server keeps control: grace is granted per ticket, so a suspension caps it immediately
  at the next refresh.

Cons and trade-offs:

- **Accepted trade-off:** a cancelled or suspended customer who goes offline right after a
  refresh can keep using the app until grace end — up to 7 days. For our price point and
  B2B market, one week of leakage is cheaper than one blocked call.
- Trial abuse window is why trial grace is only 2 days.
- Grace depends on trusting the local clock inside the window; ClockGuard and the server's
  10-minute skew rejection limit tampering.

Follow-up work: roadmap Phase 5 implements `graceUntil` capping (tested); Phase 6 implements
the client fallback and offline simulation tests.
