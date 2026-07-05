# AdaVoice Security Design

Purpose: threat model, auth and signing design, secrets, audit, and hardening for the AdaVoice monetization backend and WPF client. Status: Proposed, 2026-07-05.

Source of truth for names and values: the canonical monetization brief. Companion docs: `licensing-design.md`, `wpf-client-integration.md`.

---

## 1. Threat model

Scope: the licensing/auth backend (`server/`), the WPF client's licensing module, and the data between them. Out of scope for MVP: payment-provider integration security (Later, when webhooks arrive) and the customer's own machine security.

Assumptions:

- The attacker can read every byte of the client (it decompiles cleanly).
- The attacker controls their own machine, clock, and network.
- The server, its env vars, and the database are trusted infrastructure we control.

| Threat | Mitigation | Residual risk |
|---|---|---|
| Ticket forgery | JWS ES256; private keys only on the server (`signing_keys`, encrypted at rest); client verifies against pinned public keys | Server key compromise. Contained by rotation + 24 h ticket TTL. |
| Ticket copied to another machine | Ticket binds `deviceId` + `deviceActivationId`; client checks DPAPI-stored `deviceId` and re-computes `machineHash`; DPAPI CurrentUser makes the files unreadable off-machine | A patched client can skip the check. Accepted: client checks are UX (section 2); 24 h TTL limits the value of one stolen ticket. |
| Clock rollback (extend grace) | Client `clock.bin` guard: `lastAcceptedUtc`, 5-min tolerance, rollback → `offline_blocked`; server rejects refresh when client time skews > 10 min (`clock_skew_too_large`) and logs it | Attacker who deletes `clock.bin` and never goes online again gets at most the grace window once. Accepted for MVP. |
| Refresh-token theft | Opaque 256-bit token; server stores only SHA-256 hash; rotation on every use; reuse of a rotated token revokes the whole family and is audit-logged; DPAPI storage on the client | Active theft plus immediate use before the victim refreshes. Family revocation caps the damage. |
| Credential stuffing / brute force | Per-IP fixed-window rate limit on `/api/auth/*`; account lockout 15 min after 10 failed logins; failed attempts audit-logged; PBKDF2 password hashing (Argon2id Later) | Slow distributed attacks. Monitor audit logs; Argon2id and alerting are Later. |
| Webhook replay (Billing v2) | Provider signature verification; idempotency by provider transaction ID stored on `payments`; webhook receipts audit-logged | Provider-side signing weaknesses. Later scope; manual billing in MVP has no webhook surface. |
| Insider admin abuse | Role-based access (`operator`/`tenant_admin`/`super_admin`); every admin action audit-logged in append-only `audit_logs`; admin panel behind cookie auth | A `super_admin` is powerful by design. Mitigate with the audit trail and few admin accounts. |
| DB leak | Password hashes (PBKDF2), refresh tokens stored as SHA-256 hashes, private signing keys encrypted with a master key from an env var; no card data ever stored (providers handle it) | Personal data (names, emails) leaks in plaintext. Backups + access control; lawyer review (open question 5). |
| MITM | TLS everywhere; client validates certificates (default validation ON, never disabled) | Corporate TLS-interception proxies. Accepted; no pinning in MVP (section 10). |
| Decompiled / patched client | None that truly works — the client is untrusted by design. Server-side enforcement, short ticket TTL, Authenticode signing (Later), light obfuscation of the licensing module at most | A determined attacker cracks the client. Accepted: the goal is raising cost, not perfection. |

---

## 2. Client-is-untrusted principle

This is the core rule of the whole design:

- **Client checks are UX.** Signature verification, device match, clock guard — they exist so honest users get fast, clear, offline-capable behavior.
- **Server checks are enforcement.** Device limits, subscription status, ticket issuing, revocation — these live only on the server, where an attacker cannot patch them.

Consequences:

- Never put a secret in the client that would let it mint entitlements (no private keys, ever).
- Never trust client-reported state for billing or limits. `POST /api/license/validate` exists for support/diagnostics, not enforcement.
- Keep tickets short-lived (24 h). A cracked client that fakes local checks still loses server-issued proof within a day of any status change.
- Spend engineering time on server-side controls (auth, audit, rate limits) before spending it on client hardening. The first is enforcement; the second is friction.

A useful mental test for any proposed check: "If an attacker deletes this line from the decompiled client, what still stops them?" If the answer is "nothing", the check must also exist on the server.

---

## 3. Auth design

- Login: email + password. Hashing: ASP.NET Core Identity `PasswordHasher` (PBKDF2). Argon2id is a **Later** upgrade.
- **Access token:** JWT, **ES256** (ECDSA P-256), lifetime **15 minutes**, `kid` header. Claims: `sub` (userId), `tenant_id`, `role` (`operator` | `tenant_admin` | `super_admin`), `jti`. Kept **in memory only** on the client.
- **Refresh token:** opaque 256-bit random value. The server stores only its SHA-256 hash in `refresh_tokens`. **Rotated on every use.** If a rotated (already-used) token is presented again, the server revokes the entire token family and writes an audit log entry — this is the token-theft tripwire. Sliding lifetime 30 days, absolute lifetime 90 days.
- Client stores the refresh token DPAPI-protected in `auth.bin`.
- Endpoints: `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `POST /api/auth/change-password`, `GET /api/auth/me`.

Why short access + rotating refresh: a leaked access token dies in 15 minutes; a leaked refresh token betrays itself the moment both the thief and the owner use it.

### Token flow rules

- The client sends the access token as `Authorization: Bearer ...` on every API call.
- On `401`, the client tries one silent `POST /api/auth/refresh`; if that also fails with `invalid_refresh_token`, it clears `auth.bin` and shows the login window.
- Logout (`POST /api/auth/logout`) revokes the refresh-token family server-side and deletes `auth.bin` client-side.
- Password change revokes all refresh tokens for the user (force re-login everywhere).

### Tenant isolation

Multi-tenancy is single-database with a `tenant_id` column and **EF Core global query filters**. Security rules that follow:

- Every tenant-owned query goes through the filter; hand-written SQL that bypasses it is forbidden without review.
- `tenant_id` always comes from the validated JWT claim, never from a request body or query string.
- `super_admin` bypasses are explicit, code-reviewed, and audit-logged.

---

## 4. License ticket signing and key rotation

- Ticket format and payload: see `licensing-design.md` section 6. JWS compact, ES256, `kid` header.
- Private ECDSA P-256 keys live in the **`signing_keys` table, encrypted at rest**, with the **master key supplied via environment variable**. The API process decrypts keys into memory at startup.
- Public keys are published two ways: **two pinned keys** (current + next) embedded in the client, and JWKS at `GET /.well-known/adavoice-jwks.json`.
- **kid rollover procedure:**
  1. Generate a new key pair; insert into `signing_keys` as "next"; publish in JWKS; ship in the next client update as the second pinned key.
  2. Keep signing with the old `kid` until client rollout is broadly done.
  3. Flip signing to the new `kid`.
  4. After all outstanding tickets expire (24 h TTL) plus a safety margin, mark the old key retired. Keep it for audit; never reuse it.
- Compromise response: flip signing immediately, retire the old key, force refresh. Old tickets die within 24 h on their own.

---

## 5. Device binding

- `deviceId`: random GUID created on first run, stored DPAPI-protected (`device.bin`). Deliberately **not** derived from hardware — it identifies the installation, not the machine.
- `machineHash`: SHA-256 over normalized soft signals: Windows `MachineGuid` (registry `HKLM\SOFTWARE\Microsoft\Cryptography`), machine name, Windows user SID, system-volume serial. **Raw signals never leave the machine; only the hash is sent.** This is both a privacy stance and a data-minimization stance.
- Ticket binds `deviceActivationId` + `deviceId`. The client re-computes `machineHash` at each validation; mismatch forces online re-activation (MVP: exact match; component-wise tolerance is Later).
- These are **soft** signals. They stop casual file copying, not a determined attacker. That is the intended cost/benefit level.

---

## 6. Secrets management

### Server

- **MVP:** environment variables for the DB connection string, JWT signing configuration, master key for `signing_keys`, and email provider credentials. No secrets in `appsettings.json` committed to the repo.
- **Later:** migrate to a secret manager (host-provided vault or HashiCorp-style store). The code should read secrets through one configuration seam so this swap touches config only.
- Private signing keys: encrypted rows in `signing_keys`, master key from env var (canonical decision).

### What must NEVER be in the client

- Private signing keys (ticket or JWT).
- Payment-provider secrets (LiqPay/WayForPay/Fondy keys).
- Database credentials, admin credentials, email credentials.
- Any value whose exposure would let a client mint entitlements or reach the DB.

### Client

- DPAPI (`ProtectedData`, **CurrentUser** scope) via the `System.Security.Cryptography.ProtectedData` package.
- Files under `%LOCALAPPDATA%\AdaVoice\license\`: `device.bin` (deviceId), `auth.bin` (refresh token), `ticket.bin` (license ticket JWS), `clock.bin` (clock-guard state).
- DPAPI CurrentUser means: another Windows user, or the same files copied to another machine, cannot decrypt them. If unprotect fails, the client treats it as first run and asks the user to log in again (see `wpf-client-integration.md`).

---

## 7. Audit logs

What is logged (all with actor, tenant, timestamp, correlation ID):

- All auth events: logins, refreshes, logouts, password changes, **failed logins**, **lockouts**, refresh-token-reuse family revocations.
- Device activations and revocations.
- License issues **and denials** (with the denial `code`).
- Admin actions (tenant/user/plan/subscription/invoice/device CRUD).
- Invoice and payment changes (issue, mark-paid, cancel, refund).
- Webhook receipts (Billing v2), including rejected/duplicate ones.

Shape: the `audit_logs` table (columns elaborated in `database-design.md`): id, `tenant_id`, actor user id, action, target type/id, details (jsonb), correlation id, `created_at`.

Immutability approach: **append-only**. There is no update or delete API for audit rows; the application layer simply has no code path to modify them. Reading is `GET /api/admin/audit-logs` (filter by tenant, actor, action, date range), `super_admin` only. Retention is handled by the daily `AuditRetentionJob` worker (retention length: business/lawyer decision). DB-level hardening (separate write-only role, partition drops for retention) is Later.

---

## 8. Rate limiting and lockout

- ASP.NET Core `RateLimiter` middleware.
- **Per-IP fixed window** on `/api/auth/*` — blunt but effective against stuffing.
- **Per-device token bucket** on `/api/license/*` — a well-behaved client calls these a few times a day; a tight bucket costs honest users nothing.
- Account lockout: **15 minutes after 10 failed logins (per user)**, also written to `audit_logs`.
- Every failed attempt is logged with IP and correlation ID so patterns are visible.
- Rate-limit responses use HTTP 429 with `Retry-After`, so a well-behaved client backs off instead of hammering.
- Lockout responses do not reveal whether the account exists (same generic message as a wrong password) to avoid user enumeration.

### Idempotency as an integrity control

The `Idempotency-Key` header is required on `POST /api/devices/activate`, `POST /api/license/issue`, `POST /api/invoices`, and `POST /api/invoices/{id}/mark-paid`. The server stores key + response hash in `idempotency_keys` for 24 h. This is not only a UX nicety: it prevents a network retry from double-consuming a device slot or double-marking an invoice paid, which would corrupt billing truth. Webhooks (Later) are idempotent by provider transaction ID stored on `payments`.

---

## 9. Operational security and reliability

- **Structured logging:** Serilog on the server (matches the client's existing choice). No passwords, tokens, or raw machine signals in logs.
- **Correlation IDs:** middleware assigns/propagates `X-Correlation-Id` on every request; it flows into logs and audit rows. One ID ties a client complaint to server-side evidence.
- **Centralized error tracking:** Sentry or self-hosted GlitchTip (open question 8 — budget decision). Either way, unhandled exceptions must land somewhere watched.
- **Health checks:** `/healthz` liveness (process is up) plus a readiness check that verifies DB connectivity. Hosting/monitoring probes both.
- **DB backups:** managed PostgreSQL with PITR preferred; otherwise scheduled `pg_dump` (open question 11). Either way: **restore testing is part of the plan** — an untested backup is a hope, not a backup. Schedule a restore drill at least quarterly.
- **Background jobs as safety nets:** `TicketCleanupJob` (daily) purges expired `license_tickets` rows so the revocation-check table stays small; `AuditRetentionJob` (daily) applies the retention policy; `SubscriptionExpiryJob` (hourly) moves statuses so entitlement never depends on a human remembering to click something.

### Incident basics (MVP-sized)

- Keep an ordered list of "break glass" actions: revoke a device, suspend a tenant, rotate the signing key, revoke a refresh-token family. All exist as admin operations and are audit-logged.
- Because ticket TTL is 24 h, most licensing incidents self-heal within a day once the server-side fix lands. That is a deliberate property, not luck.

---

## 10. WPF client hardening

Realistic expectations first: **a determined attacker can crack any desktop client.** .NET decompiles cleanly. The goal is to raise the cost above the price of a subscription, not to win an arms race.

- **Authenticode code signing (recommended, Later):** sign the installer and executables. This is mainly a trust/anti-tamper/SmartScreen benefit for customers. Certificate purchase (OV/EV) is open question 7.
- **Obfuscation:** at most, **light obfuscation of `AdaVoice.Licensing` only**. Diminishing returns are steep: obfuscation breaks stack traces and debugging, complicates crash triage, and delays a motivated attacker by hours, not months. The real protection is server-side enforcement plus the 24 h ticket TTL. Do not obfuscate the rest of the app.
- **No secrets in the client.** Pinned *public* keys are fine — they are public. Nothing in the binary may grant entitlement by itself.
- **TLS-only, certificate validation ON.** Never ship a `ServerCertificateCustomValidationCallback` that returns true. **No certificate pinning in MVP**: pinning adds a rotation risk — a routine server certificate renewal could brick every deployed client. Standard chain validation plus a signed ticket already covers the threat that matters (forged entitlements).

---

## 11. Secure updates (Later scope)

- Recommendation: **Velopack** for auto-updates — signed packages, delta updates, HTTPS feed.
- Requirements when adopted: packages Authenticode-signed, the feed served over HTTPS only, and the updater verifies signatures before applying. An unsigned update channel would be the easiest way to inject a malicious client, so do not ship auto-update before signing is in place.

---

## 12. MVP vs Later

**MVP:** ES256 access tokens + rotating refresh tokens with family revocation, ticket signing with `signing_keys` + env-var master key, DPAPI client storage, append-only audit logs, rate limiting + lockout, correlation IDs, `/healthz`, TLS with default validation, backups.

**Later:** Argon2id, secret manager, payment webhooks (with signature verification + idempotency), Authenticode signing, Velopack updates, light licensing-module obfuscation (only if piracy is observed), DB-level audit immutability, alerting on audit anomalies.

---

## 13. Pre-launch security checklist

- [ ] No secrets in the repo or in `appsettings.json`; all from env vars.
- [ ] TLS enforced; HTTP redirects to HTTPS; client certificate validation untouched (default ON).
- [ ] Rate limiter and lockout verified with an integration test against `/api/auth/login`.
- [ ] Refresh-token reuse triggers family revocation + audit entry (test exists).
- [ ] Ticket signature verification rejects: wrong key, wrong `aud`/`iss`, tampered payload (tests exist).
- [ ] Audit log written for every path in section 7; spot-check via `GET /api/admin/audit-logs`.
- [ ] `/healthz` liveness and readiness wired into hosting monitors.
- [ ] One full backup-restore drill completed before the first paying tenant.
