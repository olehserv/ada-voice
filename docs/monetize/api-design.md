# AdaVoice Server — API Design

Purpose: define the REST API surface, conventions, DTOs, error model, idempotency, and rate limiting for the AdaVoice monetization backend. Status: Proposed, 2026-07-05.

Source of truth for names and statuses: [README.md](README.md) (the canonical brief).

## 1. General conventions

- Base URL: `https://<domain>/api` (domain is an open question). All bodies are JSON, UTF-8.
- Versioning: URL prefix `/api` only for v1. If we ever need breaking changes, we add a header-based version. Keep it simple now; do not add `/v1/` prefixes we do not need yet.
- Auth: `Authorization: Bearer <access token>` (JWT ES256, 15 min) unless an endpoint says otherwise. Roles: `operator`, `tenant_admin`, `super_admin`. "Device-bound" means the request must carry a valid access token AND reference the caller's own `deviceId`.
- Correlation: clients may send `X-Correlation-Id`; the server generates one if missing and returns it on every response. It is logged and stored in `audit_logs.correlation_id`.
- Timestamps: ISO 8601 UTC in JSON (`2026-07-05T10:00:00Z`). Ticket claims use Unix seconds, as defined in the brief.
- Errors: RFC 7807 `application/problem+json` with a stable machine `code` field.

Canonical error codes:

| Code | HTTP | Meaning |
|---|---|---|
| `validation_failed` | 400 | Body or query failed validation; details in `errors` extension |
| `unauthorized` | 401 | Missing/expired/invalid access token |
| `invalid_refresh_token` | 401 | Refresh token unknown, expired, or reused (family revoked) |
| `forbidden` | 403 | Authenticated but role/tenant does not allow this |
| `subscription_suspended` | 403 | Tenant's subscription is `suspended`; license actions refused |
| `tenant_suspended` | 403 | Tenant status is `suspended` |
| `device_revoked` | 403 | Device activation status is `revoked` or `blocked` |
| `not_found` | 404 | Entity missing or belongs to another tenant |
| `conflict` | 409 | State conflict (e.g. invoice already paid) |
| `device_limit_reached` | 409 | Plan device limit hit on activation |
| `idempotency_conflict` | 409 | Same `Idempotency-Key`, different request body |
| `clock_skew_too_large` | 400 | Client-reported time skews > 10 min from server |
| `rate_limited` | 429 | Rate limiter rejected the request; `Retry-After` header set |

## 2. Endpoint groups

### Auth

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/auth/login` | anonymous | Email + password → access + refresh tokens |
| `POST /api/auth/refresh` | anonymous (refresh token in body) | Rotate refresh token, new access token |
| `POST /api/auth/logout` | any authenticated | Revoke the presented refresh token family |
| `POST /api/auth/change-password` | any authenticated | Change own password; revokes other refresh tokens |
| `GET /api/auth/me` | any authenticated | Current user, role, tenant |

`POST /api/auth/login` — request:

```json
{
  "email": "operator@customer.ua",
  "password": "********",
  "deviceId": "8b6f3e0a-...-uuid"
}
```

Response `200`:

```json
{
  "accessToken": "eyJhbGciOiJFUzI1NiIsImtpZCI6ImsxIn0...",
  "accessTokenExpiresAt": "2026-07-05T10:15:00Z",
  "refreshToken": "b64u-opaque-256-bit-random",
  "refreshTokenExpiresAt": "2026-08-04T10:00:00Z"
}
```

Notes: `deviceId` is optional (admin panel logs in without it); when present the refresh token is bound to the device activation. After 10 failed logins the account locks for 15 minutes, and the lockout is audit-logged.

**Lockout is invisible on the public login endpoint (SEC-03, resolved 2026-07-13).** A locked account returns the *same* generic authentication-failed response as a wrong password — the response never says "locked" and never carries a `lockedUntil` field. This prevents user enumeration (an attacker must not be able to tell a real, locked account from a non-existent one); see [security-design.md §8](security-design.md#8-rate-limiting-and-lockout). The `lockedUntil` timestamp is shown only in the authenticated admin panel (a trusted screen), never on the anonymous endpoint.

`POST /api/auth/refresh` — request:

```json
{ "refreshToken": "b64u-opaque-256-bit-random" }
```

Response `200`: same shape as login. The old refresh token is rotated (single use). Reuse of a rotated token returns `401 invalid_refresh_token` and revokes the whole token family.

### Devices

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/devices/activate` | operator (own tenant) | Register this install; enforces plan device limit. Idempotency-Key required. |
| `GET /api/devices/current` | device-bound | This device's activation record |
| `GET /api/devices` | tenant_admin | List tenant devices |
| `POST /api/devices/{id}/revoke` | tenant_admin | Revoke a device activation |
| `POST /api/devices/heartbeat` | device-bound | Update `last_seen_at`, report versions |

`POST /api/devices/activate` — request:

```json
{
  "deviceId": "8b6f3e0a-...-uuid",
  "machineHash": "sha256-hex",
  "appVersion": "1.4.0",
  "osVersion": "10.0.26200"
}
```

Response `201`:

```json
{
  "deviceActivationId": "3f2c9d10-...-uuid",
  "status": "active",
  "activatedAt": "2026-07-05T10:00:05Z",
  "deviceLimit": { "used": 3, "max": 5 }
}
```

### License

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/license/issue` | device-bound | Issue a fresh 24 h ticket. Idempotency-Key required. |
| `POST /api/license/refresh` | device-bound | Re-issue before expiry (client calls ~every 12 h) |
| `POST /api/license/validate` | device-bound | Online check: ticket revoked? subscription changed? |
| `GET /api/license/current` | device-bound | Current ticket summary for this device |
| `GET /.well-known/adavoice-jwks.json` | anonymous | Public signing keys (JWKS) |

`POST /api/license/issue` — request:

```json
{
  "deviceId": "8b6f3e0a-...-uuid",
  "machineHash": "sha256-hex",
  "currentTicketJti": "prev-jti-uuid-or-null"
}
```

Response `200`:

```json
{
  "ticket": "eyJhbGciOiJFUzI1NiIsImtpZCI6ImsxIn0.eyJpc3MiOiJhZGF2b2ljZS1saWNlbnNlIi4uLn0.sig",
  "summary": {
    "jti": "new-jti-uuid",
    "plan": "standard",
    "subscriptionStatus": "active",
    "features": ["phrase_library", "hotkeys"],
    "limits": { "maxDevices": 5, "maxPhrases": 500 },
    "expiresAt": "2026-07-06T10:00:00Z",
    "graceUntil": "2026-07-12T10:00:00Z",
    "serverTime": "2026-07-05T10:00:00Z"
  }
}
```

The `ticket` string is the JWS defined in the brief (payload fields `iss`, `aud`, `jti`, `tenantId`, `userId`, `deviceActivationId`, `deviceId`, `plan`, `subscriptionStatus`, `features`, `limits`, `issuedAt`, `expiresAt`, `graceUntil`, `serverTime`). The `summary` is a convenience copy so the client does not need to parse the JWS to show UI state.

`POST /api/license/refresh` — request/response: same shapes as issue; `currentTicketJti` is required here. If the old ticket is `revoked`, the server still issues a fresh ticket when the subscription and device are valid; if not, it returns the matching 403 (`subscription_suspended`, `device_revoked`, ...). The server rejects the call with `400 clock_skew_too_large` if the client-reported time skews more than 10 minutes.

### Subscriptions

| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /api/subscriptions/current` | operator / tenant_admin | Tenant's subscription state |
| `POST /api/subscriptions/start-trial` | tenant_admin (Later: anonymous self-serve) | Start trial |
| `POST /api/subscriptions/change-plan` | tenant_admin | Change plan; v1 takes effect next period |
| `POST /api/subscriptions/cancel` | tenant_admin | Cancel; runs to period end |
| `POST /api/subscriptions/renew-manually` | super_admin | Admin-side renewal fix-up |

`GET /api/subscriptions/current` — response `200`:

```json
{
  "subscriptionId": "a1b2c3d4-...-uuid",
  "plan": { "code": "standard", "name": "Standard", "maxDevices": 5, "maxPhrases": 500 },
  "status": "active",
  "currentPeriodStart": "2026-07-01T00:00:00Z",
  "currentPeriodEnd": "2026-08-01T00:00:00Z",
  "trialEndsAt": null,
  "cancelledAt": null
}
```

### Billing

| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /api/invoices` | tenant_admin (own) / super_admin (any) | List invoices |
| `GET /api/invoices/{id}` | tenant_admin / super_admin | Invoice detail |
| `POST /api/invoices` | super_admin | Create invoice. Idempotency-Key required. |
| `POST /api/invoices/{id}/mark-paid` | super_admin | Record manual bank payment. Idempotency-Key required. |
| `POST /api/invoices/{id}/cancel` | super_admin | Cancel a draft/issued invoice |
| `GET /api/payments` | super_admin | List payments |
| `POST /api/payments/webhooks/liqpay` | anonymous + provider signature | LiqPay callback (Later) |
| `POST /api/payments/webhooks/wayforpay` | anonymous + provider signature | WayForPay callback (Later) |
| `POST /api/payments/webhooks/fondy` | anonymous + provider signature | Fondy callback (Later) |

`POST /api/invoices` — request:

```json
{
  "tenantId": "t-uuid",
  "subscriptionId": "a1b2c3d4-...-uuid",
  "amountUah": 1500.00,
  "periodStart": "2026-08-01T00:00:00Z",
  "periodEnd": "2026-09-01T00:00:00Z",
  "dueAt": "2026-08-05T00:00:00Z",
  "issueNow": true
}
```

Response `201`:

```json
{
  "invoiceId": "i-uuid",
  "number": "AV-2026-0042",
  "status": "issued",
  "amountUah": 1500.00,
  "currency": "UAH",
  "issuedAt": "2026-07-25T09:00:00Z",
  "dueAt": "2026-08-05T00:00:00Z"
}
```

`POST /api/invoices/{id}/mark-paid` — request:

```json
{
  "amountUah": 1500.00,
  "receivedAt": "2026-08-03T14:20:00Z",
  "reference": "bank statement line 17"
}
```

Response `200`:

```json
{
  "invoiceId": "i-uuid",
  "status": "paid",
  "paidAt": "2026-08-03T14:20:00Z",
  "payment": { "paymentId": "p-uuid", "provider": "manual_bank_transfer", "amountUah": 1500.00 },
  "subscription": { "status": "active", "currentPeriodEnd": "2026-09-01T00:00:00Z" }
}
```

Webhook example (LiqPay-style, Later) — request:

```json
{ "data": "base64-of-provider-json", "signature": "base64-sha1-signature" }
```

Response: `200` with empty body, always fast. Processing is async (see section 5).

### Usage

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/usage/events` | device-bound | Batch-upload usage events |
| `GET /api/usage/summary` | tenant_admin | Aggregated usage per period |
| `GET /api/usage/current-period` | tenant_admin | Usage in the running period |

`POST /api/usage/events` — request:

```json
{
  "deviceId": "8b6f3e0a-...-uuid",
  "events": [
    { "type": "phrase_played", "occurredAt": "2026-07-05T09:58:00Z", "data": { "phraseId": "..." } },
    { "type": "app_started", "occurredAt": "2026-07-05T08:00:00Z", "data": null }
  ]
}
```

Response `202`: `{ "accepted": 2 }`. Server stamps `received_at` itself; client `occurredAt` is informational only.

### Admin

All under `/api/admin/...`, role `super_admin` (also consumed by the Razor `/admin` UI with cookie auth):

- Tenants: `GET/POST /api/admin/tenants`, `GET/PATCH /api/admin/tenants/{id}`, `POST /api/admin/tenants/{id}/suspend`
- Users: `GET/POST /api/admin/users`, `PATCH /api/admin/users/{id}`, `POST /api/admin/users/{id}/reset-password`
- Plans: `GET/POST /api/admin/plans`, `PATCH /api/admin/plans/{id}`
- Subscriptions: `GET /api/admin/subscriptions`, `POST /api/admin/subscriptions`, `PATCH /api/admin/subscriptions/{id}`
- Invoices/devices: same CRUD pattern; device revoke shared with tenant_admin endpoint.

### Audit

| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /api/admin/audit-logs` | super_admin | Filter by `tenantId`, `actorUserId`, `action`, `from`, `to`; paged |

## 3. Error model examples

`403` — subscription suspended (on `POST /api/license/refresh`):

```json
{
  "type": "https://adavoice.example/problems/subscription_suspended",
  "title": "Subscription is suspended",
  "status": 403,
  "detail": "The tenant's subscription is suspended for non-payment. Pay the open invoice to restore access.",
  "code": "subscription_suspended",
  "correlationId": "0f8c...",
  "subscriptionStatus": "suspended"
}
```

`409` — device limit reached (on `POST /api/devices/activate`):

```json
{
  "type": "https://adavoice.example/problems/device_limit_reached",
  "title": "Device limit reached",
  "status": 409,
  "detail": "This plan allows 5 active devices. Revoke a device or upgrade the plan.",
  "code": "device_limit_reached",
  "correlationId": "1a2b...",
  "deviceLimit": { "used": 5, "max": 5 }
}
```

## 4. Idempotency

- Header: `Idempotency-Key` (client-generated UUID), required on:
  `POST /api/devices/activate`, `POST /api/license/issue`, `POST /api/invoices`, `POST /api/invoices/{id}/mark-paid`.
- Server stores `(key, endpoint, request_hash, response_status, response_body)` in `idempotency_keys` for 24 h.
- Same key + same body → replay the stored response with the same status. No side effects run twice.
- Same key + different body → `409` with code `idempotency_conflict`.
- Webhooks do not use the header; they are idempotent by `payments.provider_tx_id` (unique per provider).

## 5. Webhook validation (Later scope, design now)

Generic rule set for all providers:

1. Verify the provider signature with the server-side secret before reading any business data. Each provider has its own scheme: LiqPay = base64(sha1(private_key + data + private_key)); WayForPay = HMAC-MD5 over ordered fields; Fondy = sha1 over ordered params with the merchant password. Implement one `IWebhookVerifier` per provider.
2. Respond `200` fast (within a few seconds). Enqueue the payload and process it async.
3. Idempotency: look up `payments` by `(provider, provider_tx_id)`. If a payment already exists, acknowledge and stop.
4. Never trust the amount or invoice reference from the callback alone. Load the invoice server-side and re-check amount, currency, and status before marking it paid.
5. Log every webhook (raw payload + verification result) to `audit_logs` with `actor_type = "system"`.

## 6. Rate limiting

ASP.NET Core `RateLimiter` middleware. Rejections return `429 rate_limited` with `Retry-After`.

| Endpoint group | Strategy | Suggested MVP limit |
|---|---|---|
| `/api/auth/*` | Fixed window per IP | 10 req/min per IP; plus account lockout 15 min after 10 failed logins |
| `/api/license/*` | Token bucket per device | Burst 5, refill 10/hour per device (normal client needs ~2/day) |
| `/api/devices/*` | Fixed window per user | 30 req/min |
| `/api/usage/events` | Fixed window per device | 60 req/hour (client batches) |
| `/api/invoices`, `/api/subscriptions/*` | Fixed window per user | 60 req/min |
| `/api/admin/*` | Fixed window per user | 120 req/min |
| Webhooks | Fixed window per IP | 60 req/min (provider IPs; allowlist Later) |

Limits are configuration values, not code constants. Tune them after we see real traffic.

## 7. MVP vs Later

- MVP: Auth, Devices, License, Subscriptions (read + cancel + admin renew), Billing (manual invoices + mark-paid), Usage upload, Admin CRUD, Audit read, idempotency, rate limiting, RFC 7807 errors.
- Later: payment webhooks (LiqPay first), self-serve `start-trial`, usage dashboards, header-based API versioning if a breaking change ever forces it, provider IP allowlists.
