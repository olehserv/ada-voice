# ADR-0005: Client Is Untrusted

## Status

Proposed

## Context

The AdaVoice desktop app runs on customer machines we do not control. A .NET WPF binary can
be decompiled, patched, and re-run by anyone with moderate skill. Any check, secret, or
enforcement rule that exists only in the client can be removed by editing the binary.

When we add licensing (ADR-0001, ADR-0002), we must decide how much to trust the client.
Teams often drift into treating client-side checks as security, or into hiding secrets in
the binary and hoping obfuscation protects them. Both drifts create a false sense of safety
and real maintenance cost.

This ADR fixes the trust model explicitly so every future feature follows the same rule.

## Decision

**The client is untrusted. All enforcement lives server-side.**

Concretely:

- The server decides entitlement: subscription status, device limits, feature flags, and
  revocation are enforced at login, activation, and ticket issue/refresh. A patched client
  can unlock its own UI, but it cannot get a valid signed ticket, extend `graceUntil`, or
  activate past the device limit.
- Client-side checks (`LicenseStateMachine`, `LicenseTicketValidator`, ClockGuard, UI gates
  in the ViewModels) are **UX, not security**. They exist to show honest, clear states to
  honest users — banners, blocked screens, Retry actions.
- **No secrets in the client.** The client holds only public signing keys (pinned current +
  next), a random `deviceId`, and its own tokens. DPAPI protects these against other local
  users, not against the machine's own administrator — and we accept that.
- **Obfuscation is not security.** We do not buy or maintain an obfuscator as a security
  control. If we ever obfuscate, it is a speed bump, and no design may depend on it.
- Server-side signals (activation counts, machine-hash mismatches, usage events, audit logs)
  are how we detect abuse — not client self-reporting alone.

## Consequences

Pros:

- Honest security model: we defend the server boundary, which we actually control.
- Simpler client: no anti-tamper machinery, no hidden keys, easier debugging and testing.
- A cracked client harms one machine's UX gating; it does not leak a master secret that
  breaks licensing for everyone.

Cons and trade-offs:

- A skilled user can patch their own binary and use premium features offline forever on that
  machine. Accepted: our B2B customers are companies with contracts, and server-side denial
  still blocks updates, new activations, and support.
- We must keep the discipline: every new feature check added to the client must also exist
  on the server if it matters commercially.

Follow-up work: code signing (Authenticode) is an open question — it protects users from
tampered installers, which is integrity for the customer, not licensing security for us.
