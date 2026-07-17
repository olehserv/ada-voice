# AdaVoice Monetization — Canonical Brief and Doc Index

Purpose: the single source of truth for names, statuses, field names, and core decisions used
by every document in this folder. If a doc disagrees with this file, this file wins — fix the
doc. Status: Phases 0–2 shipped (see handoff.md); Phase 3 onward Proposed, 2026-07-05.

## Document index

| Doc | What it covers |
|---|---|
| [architecture-overview.md](architecture-overview.md) | Current system, target system, components, risks |
| [licensing-design.md](licensing-design.md) | License model, activation, tickets, offline grace, clock guard |
| [billing-subscription-design.md](billing-subscription-design.md) | Subscription lifecycle, manual invoices, provider v2, workers |
| [security-design.md](security-design.md) | Threat model, auth, signing, secrets, audit, hardening |
| [database-design.md](database-design.md) | PostgreSQL schema, indexes, migrations, retention |
| [api-design.md](api-design.md) | REST endpoints, DTOs, errors, idempotency, webhooks |
| [wpf-client-integration.md](wpf-client-integration.md) | Client changes, storage, startup flow, UX states |
| [admin-panel-design.md](admin-panel-design.md) | Admin screens, roles, support runbooks |
| [implementation-roadmap.md](implementation-roadmap.md) | Phases 0–12, tasks, acceptance criteria |
| [open-questions.md](open-questions.md) | Open questions, assumptions, pending reviews |
| `../adr/0001..0006` | Architecture Decision Records for the six core decisions |

## Business context

- Product: AdaVoice — voice assistant for operators (plays recorded phrases into calls).
- Market: Ukraine, B2B subscription. Owner has FOP 3rd group (individual entrepreneur).
- Billing v1: manual invoices + bank transfer. Billing v2: LiqPay / WayForPay / Fondy webhooks.
- Client is untrusted. Server is the source of truth.

## Current codebase (facts, 2026-07-05)

- WPF desktop app, .NET 10. Solution `AdaVoice.slnx`, central package management
  (`Directory.Packages.props`), `TreatWarningsAsErrors=true`.
- Projects, strictly layered: `src/AdaVoice.App` (WPF UI) → `src/AdaVoice.Host` (composition
  root `EngineHost`) → `AdaVoice.Audio`, `AdaVoice.Audio.Wasapi`, `AdaVoice.Core`.
- Storage today: local files under `%LOCALAPPDATA%\AdaVoice` (`library.json`, `settings.json`,
  `audio/*.wav`, `backups/*.zip`, `logs/`). No database.
- **No** networking, HTTP, backend, auth, crypto, DPAPI, telemetry, licensing, or updater code
  anywhere. No DI container. Serilog rolling-file logging. ~360 xUnit tests green. No code
  signing configured yet.

## Canonical technology decisions

| Area | Decision |
|---|---|
| Backend | ASP.NET Core, .NET 10. New top-level folder `server/` in the same repo (monorepo). |
| Server projects | `AdaVoice.Server.Api` (REST + admin Razor Pages area), `AdaVoice.Server.Domain`, `AdaVoice.Server.Infrastructure` (EF Core, crypto, email), `AdaVoice.Server.Workers` (MVP: hosted services inside Api), `AdaVoice.Server.Tests`. Dependency direction: Api → Infrastructure → Domain. |
| Database | PostgreSQL 16. EF Core 10 + Npgsql, migrations. snake_case names, `uuid` PKs (v7 preferred), `created_at`/`updated_at timestamptz` everywhere. |
| Multi-tenancy | Single database, `tenant_id` column, EF Core global query filters. |
| Client licensing module | New project `src/AdaVoice.Licensing` (`net10.0-windows`), referenced ONLY by `AdaVoice.App`. Classes: `LicenseClient`, `LicenseTicketValidator`, `SecureStore` (DPAPI), `DeviceIdentity`, `ClockGuard`, `LicenseStateMachine`. Tests: `tests/AdaVoice.Licensing.Tests`. |
| Admin panel v1 | Razor Pages area `/admin` inside `AdaVoice.Server.Api`, cookie auth, role-based. |
| Error model | RFC 7807 `application/problem+json` + stable `code` field (e.g. `device_limit_reached`, `subscription_suspended`, `invalid_refresh_token`, `device_revoked`, `tenant_suspended`, `clock_skew_too_large`). |
| Logging (server) | Serilog structured logging, `X-Correlation-Id` middleware, centralized error tracking (choice open). |
| Rate limiting | ASP.NET Core `RateLimiter`: per-IP on `/api/auth/*`, per-device on `/api/license/*`. Account lockout: 15 min after 10 failed logins. |
| Secrets (server) | Environment variables for MVP. Private signing keys encrypted in DB (`signing_keys` table) with a master key from an env var. |
| Secrets (client) | DPAPI (`ProtectedData`, CurrentUser). Files under `%LOCALAPPDATA%\AdaVoice\license\`: `device.bin`, `auth.bin` (refresh token), `ticket.bin`, `clock.bin`. |

## Canonical auth design

- Email + password login. Passwords hashed with ASP.NET Core Identity `PasswordHasher`
  (PBKDF2; Argon2id is a later upgrade).
- Access token: JWT, **ES256**, lifetime **15 minutes**, `kid` header. Claims: `sub`,
  `tenant_id`, `role`, `jti`.
- Refresh token: opaque 256-bit random. Server stores only its SHA-256 hash. **Rotation on
  every use.** Reuse of a rotated token revokes the whole family and is audit-logged.
  Sliding lifetime 30 days, absolute 90 days. Client stores it via DPAPI; access token stays
  in memory only.

## Canonical license ticket

- Format: JWS compact (JWT-shaped), signed **ES256**, `kid` header. Client embeds the current
  + next public key and can fetch JWKS at `GET /.well-known/adavoice-jwks.json` when online.
- Payload (exact field names):

```json
{
  "iss": "adavoice-license",
  "aud": "adavoice-desktop",
  "jti": "uuid",
  "tenantId": "uuid",
  "userId": "uuid",
  "deviceActivationId": "uuid",
  "deviceId": "uuid",
  "plan": "standard",
  "subscriptionStatus": "active",
  "features": ["phrase_library", "hotkeys"],
  "limits": { "maxDevices": 5, "maxPhrases": 500 },
  "issuedAt": 1780000000,
  "expiresAt": 1780086400,
  "graceUntil": 1780691200,
  "serverTime": 1780000000
}
```

- Ticket TTL: **24 hours**. `graceUntil`: paid = issue + **7 days**; trial = issue + **2 days**
  (configurable 1–3 per plan); never past the subscription's own hard end.
- Client refresh policy: on startup and when >50% of TTL has passed (~every 12 h). Failures
  fall back to the cached ticket while `now <= graceUntil`.

## Canonical device identity

- `deviceId`: random GUID from first run, DPAPI-protected. Not derived from hardware.
- `machineHash`: SHA-256 over soft signals (Windows `MachineGuid`, machine name, user SID,
  system-volume serial). Raw signals never leave the machine.
- Ticket binds to `deviceActivationId` + `deviceId`. Hash mismatch → online re-activation
  (MVP: exact match; tolerant matching is Later).

## Canonical clock-rollback guard

- Client persists `lastAcceptedUtc` (max of server/local time seen) in `clock.bin`, updated on
  each validation and every ~10 min. If `now < lastAcceptedUtc − 5 min` → `offline_blocked`
  until an online refresh clears it. Server rejects refresh when client clock skews > 10 min.

## Canonical statuses (exact values)

- Subscription: `trial`, `active`, `past_due`, `grace_period`, `suspended`, `cancelled`, `expired`
- Tenant: `active`, `suspended`, `cancelled`, `deleted`
- Device: `active`, `revoked`, `blocked`, `expired`
- Invoice: `draft`, `issued`, `paid`, `overdue`, `cancelled`, `refunded`
- Payment providers: `manual_bank_transfer`, `liqpay`, `wayforpay`, `fondy`
- User roles: `operator`, `tenant_admin`, `super_admin`

## Canonical tables

`tenants`, `users`, `plans`, `subscriptions`, `device_activations`, `license_tickets`,
`invoices`, `payments`, `usage_events`, `audit_logs`, `refresh_tokens`, `signing_keys`,
`idempotency_keys` — columns in [database-design.md](database-design.md).

## Worker jobs

`SubscriptionExpiryJob` (hourly), `InvoiceReminderJob` (daily), `TicketCleanupJob` (daily),
`AuditRetentionJob` (daily) — details in
[billing-subscription-design.md](billing-subscription-design.md).

## Client UX states (exact names)

`active`, `trial`, `grace_period`, `past_due`, `suspended`, `expired`, `device_revoked`,
`device_limit_reached`, `offline_allowed`, `offline_blocked` — behavior table in
[wpf-client-integration.md](wpf-client-integration.md). Rule of thumb: warn without blocking
during `grace_period`/`past_due`; block premium playback for
`suspended`/`expired`/`device_revoked`/`offline_blocked`; never delete local user data.

## MVP vs Later (global)

- **MVP** (roadmap phases 0–11): manual admin-created tenants/users, manual invoices + bank
  transfer, mark-paid in admin panel, licensing + activation + offline grace, Razor admin
  panel, audit logs, rate limiting.
- **Later** (phase 12+): payment provider webhooks (LiqPay first), self-serve signup + trial,
  tolerant machine-hash matching, separate Workers deployment, SPA admin, Argon2id, usage
  analytics, secret manager, auto-updater (Velopack) + Authenticode code signing.
