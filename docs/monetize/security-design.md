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

---

## 14. Implementation pitfalls checklist

This section is different from the threat model (section 1). The threat model says what an
attacker can do. This list says what **we** are likely to get wrong while writing the code.
Each item: the pitfall, why it happens, how to avoid it, and the roadmap phase where it bites.
Grouped by area; the most expensive mistakes come first inside each group.

### Auth and JWT (Phase 2)

1. **Default clock skew silently extends token lifetime.**
   Why: `TokenValidationParameters.ClockSkew` defaults to 5 minutes, so a "15-minute" access
   token really lives 20 minutes.
   Avoid: set `ClockSkew` explicitly (1 minute is enough server-side) and assert the real
   lifetime in an integration test. Phase 2.
2. **Accepting whatever `alg` the token header claims.**
   Why: libraries validate with "the key you gave me" and some accept algorithm downgrades.
   Avoid: set `ValidAlgorithms = ["ES256"]` on the server. In the client validator, hardcode
   ES256 and reject `none` or any other header value before touching the payload. Phase 2 / 6.
3. **Refresh rotation race logs out honest users.**
   Why: the client fires two refreshes at once (startup refresh + a 401-triggered refresh).
   The second call presents an already-rotated token and trips the family-revocation tripwire.
   Avoid: client — one single-flight gate (a `SemaphoreSlim`) around refresh in
   `LicenseClient`, so only one refresh is ever in flight. Server — do the rotation in one
   transaction with a row lock (`SELECT ... FOR UPDATE` on the token row), so two concurrent
   uses of the same token cannot both succeed. Phase 2 and 6.
4. **User enumeration through different answers.**
   Why: "wrong password" and "no such user" naturally take different code paths, with
   different messages and different response times (hashing vs no hashing).
   Avoid: one generic message for both; when the email is unknown, still hash a dummy
   password so timing looks the same. Phase 2.
5. **Lockout counter updated read-modify-write.**
   Why: `failed_login_count++` in C# after a read lets parallel attempts skip the lockout.
   Avoid: one conditional `UPDATE ... SET failed_login_count = failed_login_count + 1`
   (or `ExecuteUpdate`) so the database does the counting. Phase 2.
6. **Tokens and passwords leak into logs.**
   Why: request logging middleware or Serilog destructuring (`{@Request}`) captures bodies;
   login and refresh bodies contain passwords and refresh tokens.
   Avoid: never log request bodies on `/api/auth/*`; never destructure auth DTOs; redact the
   `Authorization` header in any HTTP logging handler (server and `LicenseClient`). Phase 2 / 6.

### License tickets and signing keys (Phase 5)

7. **Wrong ECDSA signature format in the client validator.**
   Why: JOSE (JWS) uses the raw `R||S` concatenation (`IeeeP1363FixedFieldConcatenation` in
   .NET), while much example code produces or expects DER. A mismatch either rejects every
   valid ticket — or tempts you to write a lenient parser that accepts both.
   Avoid: use `ECDsa.VerifyData(..., DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`
   exactly; round-trip test: server-signed ticket must verify in the client validator. Phase 5 / 6.
8. **Failing open on an unknown `kid`.**
   Why: "we could not find the key" feels like an infrastructure error, and error paths tend
   to default to letting the user through.
   Avoid: unknown `kid` → one JWKS refresh attempt → then treat as *no ticket* (fail closed).
   The design already says this; write the test that proves it. Phase 6.
9. **Missing master key falls back to a dev key.**
   Why: developers add a fallback so the app starts locally without env vars, and the
   fallback ships.
   Avoid: no fallback. If the `signing_keys` master-key env var is missing or wrong, the API
   must refuse to start with a clear error. Local dev sets the var via `docker-compose`/user
   secrets. Phase 5.
10. **A debug bypass in the validator ships to production.**
    Why: `#if DEBUG return valid` (or a config flag) is very convenient during Phase 6 UI work.
    Avoid: no bypass inside `LicenseTicketValidator` — ever. For UI work, use a locally
    generated test key pair and real signed tickets (the test project already needs that
    key-generation helper anyway). Phase 6.
11. **Ticket issue is not one transaction.**
    Why: sign → insert `license_tickets` → audit → return are four steps; a crash in the
    middle leaves a valid signed ticket the server does not know about (revocation misses it).
    Avoid: insert the `license_tickets` row and the audit row in one transaction, commit,
    then return the JWS. Phase 5.

### Client storage and DPAPI (Phase 6)

12. **Plaintext hits the disk before protection.**
    Why: the temp-then-rename pattern invites "write temp file, then protect".
    Avoid: `ProtectedData.Protect` the bytes **in memory**, then atomically write the already
    protected bytes. Plaintext never touches the disk. Phase 6.
13. **One unhandled `CryptographicException` path crashes startup.**
    Why: DPAPI unprotect fails on corrupt files, restored images, or a different Windows
    user — and it is easy to wrap three of the four read sites and forget the fourth.
    Avoid: put the catch inside `SecureStore.Read` itself: failure returns "no data" and
    quarantines the file. Callers then follow the per-file recovery rules (re-login,
    re-issue, re-activate). Phase 6.
14. **The access token gets persisted "by accident".**
    Why: binding a token to a serialized settings object, or logging it while debugging.
    Avoid: the access token lives in one private field in `LicenseClient` and appears in no
    DTO that is ever serialized. Phase 6.
15. **A staging TLS shortcut ships.**
    Why: staging has a self-signed cert, someone adds
    `ServerCertificateCustomValidationCallback = ... => true` "temporarily".
    Avoid: never add the callback, even behind a flag. Give staging a real certificate
    (Let's Encrypt is free). Grep for the callback name in CI if needed. Phase 6.

### Database and multi-tenancy (Phases 1, 3)

16. **Global query filters do not cover writes or raw SQL.**
    Why: EF Core filters apply to LINQ *queries* only. Inserts, `Attach`/`Update` by id, and
    `FromSqlRaw` all skip them.
    Avoid: `tenant_id` is set from the JWT claim in one shared place (interceptor or base
    service), never from a request body; `FromSqlRaw`/`IgnoreQueryFilters` are forbidden
    without review (grep for them in CI); load-then-modify instead of blind `Update`. Phase 1 / 3.
17. **Background jobs meet tenant filters that expect a logged-in user.**
    Why: the filter reads the tenant from a request-scoped provider; workers have no request,
    so the filter silently matches nothing — or everything.
    Avoid: decide the worker story in Phase 1: jobs iterate tenants explicitly and set the
    tenant context per batch, or use `IgnoreQueryFilters()` deliberately with a comment and
    an audit row. A test proves a job cannot read across tenants by accident. Phase 8.
18. **String interpolation into raw SQL.**
    Why: `FromSqlRaw($"... {name}")` compiles and works — and is SQL injection.
    Avoid: `FromSqlInterpolated` (parameterizes automatically) on the rare reviewed raw-SQL
    spots; never `FromSqlRaw` with interpolation. Phase 1+.
19. **The seeded super_admin is the weakest door.**
    Why: seed passwords end up as `admin123` in a compose file, or get logged at startup.
    Avoid: password comes from an env var with no default; the seeder never logs it; first
    login forces a change (the design already requires this — implement it, do not defer). Phase 1.

### API layer (Phases 2–7)

20. **problem+json leaks internals.**
    Why: putting `ex.Message` into `detail` is the quickest way to debug — and it leaks
    connection strings, table names, and stack fragments.
    Avoid: one global exception handler: generic `detail` + `correlationId` to the caller,
    full exception to Serilog only. Test: a forced 500 contains no exception text. Phase 2.
21. **Idempotency check-then-insert race.**
    Why: "SELECT key; if missing, do work; INSERT key" lets two concurrent retries both pass
    the SELECT — double activation, double payment.
    Avoid: INSERT the `idempotency_keys` row **first**, inside the same transaction as the
    side effect, relying on the `(key, endpoint)` unique constraint; on unique violation,
    read and replay the stored response (retry briefly if the first request is still running). Phase 4 / 7.
22. **Per-IP rate limiting behind a reverse proxy.**
    Why: behind nginx/Cloudflare every request has the proxy's IP — so one bucket for all
    users (self-DoS), or, if you read `X-Forwarded-For` naively, attackers spoof unlimited
    fresh IPs.
    Avoid: configure `ForwardedHeadersMiddleware` with `KnownProxies`/`KnownNetworks` for the
    real deployment topology; only then partition by client IP. Verify on staging, not in
    production. Phase 2 / 10.
23. **404 vs 403 leaks cross-tenant existence.**
    Why: "entity exists but is not yours" naturally returns 403, which confirms existence.
    Avoid: the design already says `not_found` for other tenants' entities — implement it as
    one shared lookup helper (`GetOwnedOrNotFound`) so no endpoint hand-rolls the check. Phase 3.

### Admin panel (Phase 9)

24. **State changes in GET handlers.**
    Why: a "Revoke" link (`<a href=...>`) calls `OnGet` — no antiforgery token protects GET,
    so any web page the admin visits can trigger it (CSRF).
    Avoid: every mutation is a `<form method="post">` with an `OnPost*` handler; Razor Pages
    antiforgery stays at its default (on); never call `IgnoreAntiforgeryToken`. Phase 9.
25. **The admin cookie also authenticates `/api/admin/...`.**
    Why: with cookie + JWT schemes both registered, the default policy may accept either —
    then a CSRF page can call the JSON API with the admin's cookie.
    Avoid: bind schemes explicitly: `/admin` pages → cookie scheme; all `/api/...` →
    JWT bearer scheme only. Set the cookie `Secure`, `HttpOnly`, `SameSite=Lax` or stricter.
    Test: an `/api/admin/...` call with only a cookie gets 401. Phase 9.
26. **A new admin page forgets `[Authorize]`.**
    Why: per-page attributes rely on memory.
    Avoid: one folder convention — `AuthorizeFolder("/admin", "SuperAdminOnly")` — so pages
    are protected by location, not by discipline. A smoke test hits every page anonymously
    and expects a redirect. Phase 9.

### Billing and workers (Phases 7, 8, 12)

27. **Mark-paid is not atomic.**
    Why: payment row, invoice status, subscription extension, and audit row are four writes;
    a crash between them corrupts billing truth (money received, service not extended).
    Avoid: one transaction for all four; the idempotency key row commits with it. Phase 7.
28. **Overlapping job runs double-process.**
    Why: a slow hourly run overlaps the next tick; both move the same subscription.
    Avoid: a Postgres advisory lock per job (`pg_try_advisory_lock`), taken at run start;
    plus state+time-based selection so a re-run is a no-op (the design already requires
    idempotent jobs — the lock covers the concurrent case). Phase 8.
29. **Webhook signature compared with `==`.**
    Why: string comparison short-circuits, which leaks timing; and it is tempting to parse
    the payload first to find the invoice, then verify.
    Avoid: verify the provider signature **before** deserializing anything, using
    `CryptographicOperations.FixedTimeEquals`; only then read business data. Phase 12.

### How to use this list

- Turn each item that applies to a phase into a test or a code-review point when that phase
  starts. Most items above name their test.
- At the end of each phase, walk the group for that phase the same way section 13 is walked
  before launch.
