# Admin Panel Design

**Purpose:** define the admin panel for AdaVoice monetization: technology, screens, roles, and support workflows. The panel is how the owner runs the business day to day.

**Status: Proposed, 2026-07-05**

---

## 1. Technology

- Razor Pages area `/admin` inside `AdaVoice.Server.Api`. No separate frontend project.
- Cookie authentication for admins. This is separate from the JWT auth used by the desktop app.
- Role checks on every page and every handler:
  - `super_admin` — full access to all tenants and all screens.
  - `tenant_admin` — sees only their own tenant.
- **Important scope note:** the v1 admin panel is **super_admin-only**. Tenant self-service
  (a portal where `tenant_admin` manages their own users and devices) is **Later** scope.
  We still design role checks now, so the same pages can open up later without a rewrite.
- The panel calls the same application services as the `/api/admin/...` REST endpoints.
  Pages hold no business logic. This keeps one code path for every action.
- Serilog logging and the correlation ID middleware (`X-Correlation-Id`) cover the panel too.

### Why Razor Pages, not a SPA

- One deployable. One auth story. No CORS, no separate build pipeline.
- The panel is a low-traffic internal tool. Server-rendered pages are enough.
- If the panel grows, we move to a SPA **Later**. The service layer stays the same.

---

## 2. Minimum required screens (MVP)

### 2.1 Dashboard
- Counts at a glance:
  - active tenants
  - subscriptions expiring soon (next 14 days)
  - overdue invoices
  - recent failures (failed logins, rejected license refreshes, webhook errors — from `audit_logs`)
- Each count links to the filtered list behind it.

### 2.2 Tenant list / detail
- List: name, status (`active`, `suspended`, `cancelled`, `deleted`), plan, created date. Search by name.
- Detail: tenant info, current subscription, users, devices, invoices.
- Actions: **create tenant**, **suspend tenant**, reactivate.

### 2.3 User list (per tenant)
- List: email, role (`operator`, `tenant_admin`, `super_admin`), lockout state, last login.
- Actions: **create user**, **reset password** (generates a one-time temporary password),
  **lock** / unlock account.

### 2.4 Device list (per tenant)
- List: device name hint, status (`active`, `revoked`, `blocked`, `expired`), last heartbeat,
  app version, activation date.
- Actions: **revoke** (frees a device slot; normal flow) and **block** (device may not
  re-activate; abuse flow).

### 2.5 Subscription view (per tenant)
- Current status (`trial`, `active`, `past_due`, `grace_period`, `suspended`, `cancelled`,
  `expired`), plan, period start/end, device limit and usage.
- Actions: **start trial**, **change plan**, **cancel**, **renew manually**.
- Every action shows the resulting status before you confirm.

### 2.6 Invoice list / detail
- List: number, tenant, amount, status (`draft`, `issued`, `paid`, `overdue`, `cancelled`,
  `refunded`), due date. Filter by status and tenant.
- Detail: line items, linked payments, linked subscription period.
- Actions: **create**, **issue**, **mark paid**, **cancel**.
- Mark-paid uses the `Idempotency-Key` mechanism, same as the API. Double-click is safe.

### 2.7 Payment list
- List: date, tenant, invoice, amount, provider (`manual_bank_transfer` in MVP), reference.
- Read-only in v1. Payments are created by the mark-paid action.

### 2.8 Audit log viewer
- Filters: **tenant, actor, action, date range** (matches `GET /api/admin/audit-logs`).
- Shows: timestamp, actor type, actor, action, target, correlation ID, details.
- Read-only. This is the first place to look when a customer reports a problem.

### 2.9 Signing keys page
- View keys from `signing_keys`: `kid`, status (current / next / retired), created date.
- Action: **trigger rotation** (creates the next key; server keeps signing with the old key
  until clients have the new public key; retire old after the 24 h ticket TTL window).
- This page is deliberately simple. Rotation is rare and must be hard to do by accident:
  the action requires a typed confirmation.

---

## 3. Roles and permissions matrix

v1 reality: only `super_admin` can log in to the panel. The `tenant_admin` column shows the
**Later** design so we build role checks correctly from day one. "Own" = own tenant only.

| Screen | super_admin | tenant_admin (Later) |
|---|---|---|
| Dashboard | Full (all tenants) | Own tenant summary |
| Tenant list/detail | Full + create/suspend | Own tenant, read-only |
| User list | Full + create/reset/lock | Own users + create operator, reset password |
| Device list | Full + revoke/block | Own devices + revoke (no block) |
| Subscription view | Full + all actions | Own, read-only (contact support to change) |
| Invoice list/detail | Full + create/issue/mark paid/cancel | Own invoices, read-only |
| Payment list | Full | Own payments, read-only |
| Audit log viewer | Full | Own tenant events only |
| Signing keys | Full | No access |

`operator` never has panel access.

---

## 4. Support workflows (runbooks)

Short, ordered steps. Each step maps to a screen above.

### 4.1 Customer paid by bank transfer
1. Open **Invoice list**, find the invoice (filter: tenant + `issued` or `overdue`).
2. Verify the amount against the bank statement.
3. Click **Mark paid**. This creates a `payments` row (`manual_bank_transfer`) and updates the invoice to `paid`.
4. Open **Subscription view**. Confirm the status moved to `active` (or the new period end is set).
5. Tell the customer: the app refreshes its license within 24 h automatically, or they can click **Refresh** in the app right away.

### 4.2 Operator PC replaced
1. Open **Device list** for the tenant.
2. Find the old device (usually the one with the oldest heartbeat). Click **Revoke**. This frees one device slot.
3. Ask the customer to start AdaVoice on the new PC and log in. Activation happens automatically.
4. If activation fails with `device_limit_reached`, re-check the device list — another slot may need revoking.

### 4.3 Customer reports a blocked app
1. Open **Subscription view**. Check the status. `suspended` or `expired` explains the block.
2. Open **Device list**. Check the device status. `revoked` or `blocked` also explains it.
3. Open **Audit log viewer**, filter by tenant + date. Look for the denial reason
   (e.g. `subscription_suspended`, `device_revoked`, `clock_skew_too_large`).
4. Fix the root cause (mark invoice paid, un-revoke by re-activating, or explain the clock issue), then ask the customer to click **Retry/Reconnect** in the app.

### 4.4 Suspected license abuse
1. Open **Device list** for the tenant. Compare the active device count with the plan limit and with the number of real operators.
2. Open the **Audit log viewer** and usage events. Look for many activations, machine-hash mismatches, or heartbeats from too many devices.
3. If abuse is confirmed: **Revoke** the extra devices and **Block** the offending ones so they cannot re-activate.
4. Contact the customer. Document the case in the audit trail (block actions are audit-logged automatically).

---

## 5. Auditing rule

**Every admin action writes to `audit_logs` with `actor_type=admin`.**

- No exceptions. Create, suspend, revoke, block, mark-paid, key rotation — all of them.
- The audit row includes: actor user ID, action, target entity + ID, timestamp, correlation ID, and a small details payload (e.g. old status → new status).
- This is not optional polish. Manual billing means human actions move money; the audit log is our only reliable history.

---

## 6. MVP vs Later

### MVP
- All screens in section 2, super_admin-only.
- Cookie auth, lockout after failed logins (same policy as API: 15 min after 10 failures).
- Audit logging of every action.
- Idempotent mark-paid.

### Later
- Tenant self-service portal (`tenant_admin` access per the matrix in section 3).
- Email automation (invoice PDFs, payment reminders — `InvoiceReminderJob` sends them; the panel only shows send history).
- Usage analytics dashboards (charts over `usage_events`).
- SPA rewrite, only if the panel demonstrably outgrows Razor Pages.

---

## 7. Assumptions

- **Assumption:** temporary passwords from "reset password" are one-time and force a password change at next login.
- **Assumption:** "expiring subscriptions" on the dashboard means period end within 14 days. Adjust once we see real renewal behavior.
