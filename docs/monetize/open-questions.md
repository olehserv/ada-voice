# Open Questions — AdaVoice Monetization

Purpose: one consolidated list of open questions, design assumptions, and pending reviews for the monetization work. Status: Proposed, 2026-07-05.

How to read this doc:

- Each item has an ID. Reference the ID in commits, ADRs, and design docs.
- "Blocks" points at roadmap phases 0–12 (see `implementation-roadmap.md`): 0 repository preparation, 1 domain model + database, 2 auth, 3 tenant/user/subscription core, 4 device activation, 5 license issuing and validation, 6 WPF integration, 7 manual billing, 8 background workers, 9 admin panel, 10 security hardening, 11 pilot launch, 12 payment provider integration.
- An item marked "Blocks: none (MVP)" can be answered later without stopping MVP work.

---

## 1. Business decisions (owner)

| ID | Question | Why it matters | Who decides | Blocks |
|---|---|---|---|---|
| OQ-01 | Hosting location and provider (EU VPS? Ukrainian provider?) | Affects latency for Ukrainian operators, data-protection law, cost, and backup options. | Owner | Phase 11 (pilot deploy); pick a cheap dev/staging host during Phase 0 without blocking. |
| OQ-02 | Domain / API base URL | The client pins the API base URL and JWKS URL. Changing it later means a client update. | Owner | Phase 6 (WPF integration) needs at least a stable staging URL; Phase 11 needs the final one. |
| OQ-03 | Pricing, plan matrix, seat vs device pricing, trial length | Defines the `plans` table content, `limits.maxDevices`, and trial `graceUntil` (2 days, configurable 1–3). Code can ship with placeholder plans. | Owner | Phase 7 (first real invoices); not Phases 1–6. |
| OQ-06 | Payment provider order (LiqPay vs WayForPay vs Fondy) and their FOP support | Decides which webhook (`/api/payments/webhooks/liqpay` etc.) we build first. Brief's working order: LiqPay first. | Owner | Phase 12 (payment webhooks). |
| OQ-07 | Code-signing certificate (OV/EV) purchase | Unsigned installers trigger SmartScreen warnings and hurt trust. Cost/benefit decision. | Owner | Phase 10 (security hardening); the pilot installer (Phase 11) should be signed if possible. |
| OQ-08 | Error tracking: Sentry SaaS vs self-hosted GlitchTip | Budget vs maintenance effort. Server logging design leaves this pluggable. | Owner | Phase 10; Serilog files are enough before that. |
| OQ-09 | Email provider for invoices/reminders (Postmark / Resend / SMTP) | `InvoiceReminderJob` and invoice delivery need a sender. Infrastructure hides it behind an email abstraction. | Owner | Phase 7 (manual billing emails); Phase 8 (`InvoiceReminderJob`). |
| OQ-10 | Uninstall / data-retention promise to customers | Phrase recordings stay local (client never deletes them). But how long do we keep server-side auth and usage data after a tenant cancels? Feeds the `AuditRetentionJob` policy and the terms of service. | Owner + lawyer | Phase 11 (pilot; must be in terms before first external customer). |
| OQ-11 | Backup strategy: managed Postgres with PITR vs self-managed pg_dump | The database is the source of truth for who paid. Losing it is a business-ending event. | Owner (budget) | Phase 10 (backup + restore testing) and Phase 11 (production host). |
| OQ-12 | Per-seat licensing enforcement in v1 (one user = one concurrent device?) | Changes device-limit logic and the heartbeat design (`POST /api/devices/heartbeat`). Current design: per-plan device limit only. | Owner | Phase 4 (device activation rules) — needs an answer before licensing code freezes. |

## 2. Decisions that need explicit owner confirmation

These already have a working answer in the design. The owner must confirm or change them before the phase listed.

| ID | Working answer in design | Confirm before |
|---|---|---|
| OC-01 | Pricing and plan matrix: placeholder plans until OQ-03 is answered | Phase 7 |
| OC-02 | Hosting: undecided; design assumes one small VPS or managed host running Api + Postgres | Phase 11 |
| OC-03 | Domain: undecided; client config keeps base URL replaceable | Phase 6 (staging), Phase 11 (final) |
| OC-04 | Provider order: LiqPay first, then WayForPay, then Fondy | Phase 12 |
| OC-05 | Trial length: 2 days offline grace, trial duration itself is part of OQ-03 | Phase 7 |
| OC-06 | Device limits: per plan via `limits.maxDevices` (example: 5); no per-seat concurrency in v1 | Phase 4 |

## 3. Accountant review

| ID | Question | Why it matters | Who decides | Blocks |
|---|---|---|---|---|
| ACC-01 | FOP 3rd group invoicing rules: invoice numbering, required PDF format, tax accounting | Invoice entity fields (numbering scheme) and the PDF template must match Ukrainian FOP rules. Wrong numbering is painful to fix after real invoices exist. | Accountant | Phase 7 (before the first real invoice is issued). |

Concrete sub-questions to bring to the accountant:

- Is a strict sequential invoice number required, or is a yearly prefix scheme fine?
  This decides whether we need a gap-free number generator in the database.
- Which fields are mandatory on the invoice PDF (FOP details, tax group note, currency)?
- How must bank-transfer payments be recorded for tax reporting? Does the admin panel's
  `mark-paid` action need extra fields (payment date, bank reference)?
- Later (Phase 12): how do LiqPay/WayForPay/Fondy payouts appear in FOP accounting?

## 4. Lawyer review

| ID | Question | Why it matters | Who decides | Blocks |
|---|---|---|---|---|
| LAW-01 | Personal data of operators (names, emails) under Ukrainian data-protection law | The server stores operator emails and names in `users`. We must know the legal basis, retention limits, and breach duties. | Lawyer | Phase 11 (before pilot with external users). |
| LAW-02 | GDPR, if any customers are in the EU | GDPR adds consent, data-subject rights (export/delete), and possible hosting-location constraints (ties into OQ-01). | Lawyer | Phase 11 if EU customers are possible; otherwise before first EU sale. |
| LAW-03 | EULA / terms of service for the desktop app | Must cover license terms, offline grace behavior, feature blocking on non-payment, and the data-retention promise (OQ-10). | Lawyer | Phase 11 (must exist before the pilot contract). |

Concrete sub-questions to bring to the lawyer:

- What we store per operator: email, name, password hash, device activations, usage events,
  audit logs. Is this list acceptable, and for how long may we keep each item?
- The `machineHash` design: raw hardware signals never leave the machine, only a SHA-256
  hash is sent. Confirm this counts as pseudonymized, not raw personal data.
- Do we need a data-processing agreement with each B2B tenant (tenant employees are the
  operators)?
- If GDPR applies (LAW-02): who handles export/delete requests, and within what deadline?
- EULA must state clearly: on non-payment the app blocks premium features but never
  deletes local recordings (`%LOCALAPPDATA%\AdaVoice`).

## 5. Security review

| ID | Question | Why it matters | Who decides | Blocks |
|---|---|---|---|---|
| SEC-01 | External security review of auth + licensing before pilot: pen-test, or at least an independent expert review | Single developer, no second pair of eyes. Auth (JWT ES256, refresh rotation), license tickets (JWS), DPAPI storage, and webhook signature checks are exactly the code where a quiet mistake is expensive. | Owner (budget) + external reviewer | Phase 10 (security hardening; gate before the Phase 11 pilot). Scope minimum: `/api/auth/*`, `/api/license/*`, `/api/devices/*`, ticket validation in `AdaVoice.Licensing`. |
| SEC-02 | Design gap: `idempotency_keys` is unique on `(key, endpoint)` only, with no tenant/user scope | The stored response is replayed to whoever presents the same key. A malicious client that guesses or reuses another tenant's key on the same endpoint would receive that tenant's stored response — a cross-tenant data leak. Fix: scope the unique key to the caller, e.g. unique `(user_id, key, endpoint)`, and update `api-design.md` section 4 + `database-design.md`. | Dev (design fix, no external decider) | Phase 4 (first idempotent endpoint). |
| SEC-03 | Design contradiction: lockout response reveals the account exists | `api-design.md` (login notes) says lockout returns `403` with a `lockedUntil` extension, but `security-design.md` section 8 says lockout must look identical to a wrong password to prevent user enumeration. Both cannot hold. Recommendation: keep the generic message for the anonymous login endpoint; show `lockedUntil` only in the admin panel. Update whichever doc loses. | Dev (design fix) | ~~Phase 2 (login/lockout implementation)~~ **Resolved 2026-07-13 — see §9.** |

Suggested review checklist (minimum scope):

- Login and refresh flow: refresh-token rotation, family revocation on reuse, lockout
  (15 min after 10 failed logins), rate limiting on `/api/auth/*`.
- License ticket validation in `LicenseTicketValidator`: signature check order, `alg`
  confusion (reject anything but ES256), `aud`/`iss` checks, expiry vs `graceUntil` logic.
- Key handling: `signing_keys` encryption at rest, master key from env var, JWKS endpoint,
  pinned-key rotation path.
- Device flow: activation limits, revocation taking effect within one ticket TTL (24 h),
  `machineHash` mismatch handling.
- Clock guard: `clock.bin` tampering, DPAPI file deletion, system clock rollback.
- Admin area: cookie auth settings, CSRF on Razor Pages, role checks on `/api/admin/...`.
- Later (Phase 12): webhook signature verification and replay protection per provider.

## 6. Assumptions made in the design

Marked as **Assumption** per the brief. Each one is reversible, but the "cost to change" column says how painful.

| ID | Assumption | Why chosen | Cost to change later |
|---|---|---|---|
| AS-01 | PostgreSQL 16 as the database | Repo has no DB today (free choice). Cheaper hosting than SQL Server; first-class EF Core support via Npgsql. | Medium: EF Core hides most SQL, but migrations and snake_case naming are Postgres-flavored. |
| AS-02 | ES256 (ECDSA P-256) for access tokens and license tickets | Small signatures, fast verify, standard JOSE support. Client pins current + next public key; JWKS at `GET /.well-known/adavoice-jwks.json`. | Low–medium: `kid` header and key rotation path already exist. |
| AS-03 | DPAPI (`ProtectedData`, CurrentUser) for client secrets | Built into Windows, no key management on our side. Files: `device.bin`, `auth.bin`, `ticket.bin`, `clock.bin` under `%LOCALAPPDATA%\AdaVoice\license\`. | Low: `SecureStore` wraps it; only that class changes. |
| AS-04 | Monorepo: `server/` next to `src/` in the same repo | One developer, one CI, shared conventions, atomic cross-cutting changes. | Low: folders split cleanly into a second repo later. |
| AS-05 | Razor Pages admin area inside `AdaVoice.Server.Api` | Fastest path to a working admin panel; no separate deploy, shared auth. SPA only if the panel grows. | Medium: admin logic sits behind `/api/admin/...` endpoints, so a SPA can reuse them. |
| AS-06 | License ticket TTL = 24 hours | Short enough that key rotation and revocation propagate within a day; long enough that one refresh per workday suffices. Client refreshes at >50% TTL (~every 12 h). | Low: server-side config. |
| AS-07 | Offline grace: 7 days (paid), 2 days (trial, configurable 1–3 per plan) | Paid operators survive a week of network trouble; trials cannot be farmed offline. `graceUntil` never passes the subscription's own hard end. | Low: values in plan config. |
| AS-08 | Device limit is per plan (`limits.maxDevices` in the ticket) | Simple to enforce at `POST /api/devices/activate`; matches B2B "team of operators" reality. Per-seat concurrency deferred (OQ-12). | Medium if OQ-12 flips: needs heartbeat-based concurrency tracking. |

## 7. How a question gets closed

Keep the process light but written down:

1. Get the answer from the decider (owner, accountant, lawyer, or reviewer).
2. Record the answer in this file: move the row to a short "Resolved" list at the bottom
   with the date and a one-line decision.
3. Update the canonical brief and any affected design doc in `docs/monetize/`.
4. If the decision changes architecture (for example OQ-12 flips to per-seat), write or
   update an ADR in `docs/adr/` instead of silently editing designs.

## 8. What to do next

- Answer OQ-12 and confirm OC-06 first — they are the only items that block licensing code (Phase 4).
- Book the accountant (ACC-01) and lawyer (LAW-01..03) early; their answers arrive slowly and gate Phases 7 and 11.
- Budget and book SEC-01 before Phase 10 starts, so the review does not delay the pilot.
- Everything else can wait until its phase. Do not block MVP work on late-phase questions.

## 9. Resolved

Move rows here as decisions land, newest first, in this form:

| Date | ID | Decision (one line) |
|---|---|---|
| 2026-07-13 | SEC-03 | Lockout is invisible on the public login endpoint: a locked account returns the **same generic response as a wrong password** (no `lockedUntil`), for user-enumeration defence. `lockedUntil` is shown only in the admin panel. `security-design.md` §8 wins; `api-design.md` login notes updated. Owner: Oleh. Unblocks Phase 2. |
| 2026-07-12 | OQ-02 | Phase 0 gate: domain / API base URL **deferred** with OQ-01. Client keeps the base URL replaceable. Owner: Oleh. Needed by Phase 6 (staging URL), final by Phase 11. |
| 2026-07-12 | OQ-01 | Phase 0 gate: hosting location/provider **deferred**. MVP Phases 1–10 run on local Docker Postgres 16 (`docker-compose.yml`). Owner: Oleh. Revisit at Phase 11 (production deploy). |
