# ADR-0006: Use JWT Access Tokens with Refresh Token Rotation

## Status

Proposed

## Context

Operators log in to AdaVoice with email + password. The desktop app then talks to the API
for weeks without asking for the password again. We need a session mechanism that is
long-lived for the user, short-lived on the wire, and safe if a token is stolen from a
customer PC.

A single long-lived bearer token is the naive option: one theft gives an attacker weeks of
access, and we cannot tell the thief from the real client. Sessions with server-side lookup
on every request are another option, but they add a database hit per call and still need a
long-lived credential on the client.

The client is untrusted and its disk may be read by other software (ADR-0005), so how the
credential is stored on the client matters too.

## Decision

Two-token design:

- **Access token:** JWT signed with **ES256** (ECDSA P-256), lifetime **15 minutes**,
  `kid` header. Claims: `sub` (userId), `tenant_id`, `role` (`operator` | `tenant_admin` |
  `super_admin`), `jti`. Kept **in memory only** on the client.
- **Refresh token:** opaque 256-bit random value. The server stores only its **SHA-256
  hash** in the `refresh_tokens` table. **Rotated on every use**: each refresh call returns
  a new token and retires the old one. Sliding lifetime 30 days, absolute lifetime 90 days.
- **Reuse detection with family revocation:** if a rotated (already-used) refresh token is
  presented, the server revokes the entire token family and writes an audit row. A stolen
  token can be used once at most, and its first collision with the real client kills the
  whole session.
- **Client storage:** the refresh token is stored via **DPAPI** (`ProtectedData`,
  CurrentUser scope) in `auth.bin` under `%LOCALAPPDATA%\AdaVoice\license\`.

## Consequences

Pros:

- Stateless request auth: the API validates the 15-minute JWT with a public key — no DB hit
  per request; a stolen access token dies within minutes.
- Rotation + family revocation turns refresh-token theft from "silent long-term access"
  into a detectable, self-limiting event that appears in `audit_logs`.
- Hash-only storage means a database leak does not leak usable refresh tokens.
- ES256 with `kid` matches the license-ticket crypto (ADR-0002): one key-handling story.

Cons and trade-offs:

- More moving parts than one bearer token: rotation, families, reuse detection all need
  tests (roadmap Phase 2 test matrix).
- A flaky network can make the client miss a rotation response; the client must retry
  idempotently, and a false-positive family revocation just forces a re-login — annoying,
  not harmful.
- DPAPI protects against other users on the machine, not against the same Windows user's
  malware. Accepted under ADR-0005; the 90-day absolute lifetime bounds the damage.

Follow-up work: password hashing starts with ASP.NET Core Identity `PasswordHasher`
(PBKDF2); Argon2id is a Later upgrade.
