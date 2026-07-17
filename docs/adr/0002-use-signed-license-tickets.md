# ADR-0002: Use Signed License Tickets

## Status

Accepted — decision locked in the canonical brief (docs/monetize/README.md); implementation lands per the roadmap phases.

## Context

ADR-0001 makes the server the source of truth for licensing. But the desktop app must work
offline: operators cannot lose phrase playback mid-call because of a network blip. So the
client needs a local proof of entitlement that it can check without the server — and that
proof must be hard to forge and quick to invalidate.

Two obvious designs fail our needs. Checking online at every start gives the server full
control but breaks the offline requirement. A long-lived license file works offline but is
almost impossible to revoke: a cancelled customer keeps a valid file for months.

## Decision

The server issues **short-lived signed license tickets**: JWS compact serialization, signed
with **ES256** (ECDSA P-256), `kid` in the header. Ticket TTL is **24 hours**. The payload
carries the canonical fields (`tenantId`, `userId`, `deviceActivationId`, `deviceId`, `plan`,
`subscriptionStatus`, `features`, `limits`, `issuedAt`, `expiresAt`, `graceUntil`,
`serverTime`).

Keys rotate via `kid`: server-side key pairs live in the `signing_keys` table; the client
embeds the current + next public keys and can fetch
`GET /.well-known/adavoice-jwks.json` when online. Rotation: add new key → keep signing with
the old until clients update → retire old. The 24 h TTL makes rotation fast.

The client refreshes silently on startup and when more than 50% of the TTL has passed
(about every 12 h). If refresh fails, it falls back to the cached ticket while
`now <= graceUntil` (see ADR-0004).

Alternatives rejected:

- **Online check on every start** — rejected: violates the offline requirement.
- **Long-lived license file** (weeks or months) — rejected: revocation is far too weak;
  a suspended tenant keeps working until the file expires.

## Consequences

Pros:

- Offline validation with standard, well-audited crypto (JWS/ES256); no secret in the client,
  only public keys.
- Revocation converges within 24 h: a suspended subscription simply stops getting new tickets.
- `kid` rotation gives us a clean key-compromise recovery path.

Cons and trade-offs:

- A cancelled customer can still run until `graceUntil` fully offline (accepted; ADR-0004).
- Clock tampering becomes an attack vector; the ClockGuard design (5-minute rollback
  detection, server-side 10-minute skew rejection) mitigates it.
- We must operate key storage (encrypted in DB, master key from env var) and practice
  rotation (roadmap Phase 10 drill).

Follow-up work: roadmap Phase 5 (issue/refresh/validate endpoints, JWKS), Phase 6
(`LicenseTicketValidator`, pinned keys in the client).
