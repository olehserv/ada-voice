# ADR-0001: Use Server-Side Licensing

## Status

Proposed

## Context

AdaVoice is a WPF desktop app for call-center operators. It plays recorded phrases into live
calls. We are moving to a B2B subscription model in Ukraine. Today the app has no networking,
no auth, and no licensing code at all.

We need a way to make sure only paying tenants can use the product. The client runs on
machines we do not control. Any check that lives only inside the client can be patched,
copied, or bypassed. At the same time, operators must not be blocked during a live call
because of a network problem.

We considered several licensing models before choosing one.

## Decision

The server is the single source of truth for licensing. A new backend
(`server/AdaVoice.Server.Api`, ASP.NET Core, PostgreSQL) owns tenants, users, subscriptions,
device activations, and billing. The desktop app authenticates, activates its device, and
receives a signed, short-lived license ticket (see ADR-0002). The client only caches and
verifies what the server issued; it never decides entitlement on its own.

Alternatives considered and rejected:

- **Offline perpetual license keys.** Simple to build, but no revocation, no subscription
  lifecycle, and keys leak. A subscription business cannot stop a non-paying customer.
- **Hardware dongles.** Strong protection, but expensive, slow to ship in Ukraine, painful
  when an operator's PC is replaced, and hostile to a low-friction B2B sale.
- **Pure-online SaaS check (call home on every action).** Strongest control, but a desktop
  voice tool must keep working during calls when the network blips. Blocking an operator
  mid-call is the one failure we may never cause.

## Consequences

Pros:

- Real revocation and subscription enforcement: suspend a tenant and the license stops
  refreshing.
- One place to reason about entitlement; the client stays simple.
- Audit trail of activations, refreshes, and denials on the server.

Cons and trade-offs:

- We must build and run a backend: hosting, backups, uptime, security (Phases 0–11 of the
  roadmap). This is real ongoing cost.
- Offline behavior needs careful design; ADR-0002 (signed tickets) and ADR-0004 (grace
  period) cover this.
- The client is still untrusted (ADR-0005): a determined attacker can patch the binary.
  Server-side licensing limits the damage to single patched machines, not leaked keys.

Follow-up work: roadmap Phases 0–5 build the server; Phase 6 integrates the WPF client
behind a feature flag.
