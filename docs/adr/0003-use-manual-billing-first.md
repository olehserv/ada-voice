# ADR-0003: Use Manual Billing First

## Status

Accepted — decision locked in the canonical brief (docs/monetize/README.md); implementation lands per the roadmap phases.

## Context

AdaVoice sells B2B subscriptions in Ukraine. The owner operates as a FOP 3rd group
(individual entrepreneur). At this stage there are zero paying customers; the first goal is
to reach first revenue with the least scope.

Ukrainian B2B customers commonly pay by bank transfer against an invoice. Online payment
providers (LiqPay, WayForPay, Fondy) exist, but integrating one means webhooks, signature
verification, reconciliation, sandbox testing, provider onboarding for a FOP, and legal or
accounting questions — all before the first hryvnia arrives.

We must choose: build provider integration now, or start with manual billing.

## Decision

Billing v1 is **manual**: the owner creates invoices in the admin panel, the customer pays by
**bank transfer**, and the owner clicks **mark paid**. Mark-paid records a `payments` row
with provider `manual_bank_transfer` and applies the renewal effect to the subscription
(e.g. `suspended → active`, period extended).

We design the data model provider-ready from day one: `payments` carries a provider field
with canonical values `manual_bank_transfer`, `liqpay`, `wayforpay`, `fondy`, and webhook
routes are reserved (`POST /api/payments/webhooks/liqpay` and siblings). Provider
integration is roadmap Phase 12 — after the MVP cut line, LiqPay first.

Alternative rejected:

- **Integrate LiqPay immediately.** Rejected because it is slower to first revenue and adds
  scope we do not need for the first handful of tenants. Early B2B customers pay by bank
  transfer anyway. Manual mark-paid teaches us the real billing workflow before we
  automate it.

## Consequences

Pros:

- Fastest path to first revenue; billing works as soon as the admin panel does
  (roadmap Phases 7 and 9).
- Matches how Ukrainian B2B customers already pay a FOP.
- The manual loop becomes the tested fallback even after providers are added.

Cons and trade-offs:

- Human work per payment: find invoice, verify bank statement, mark paid. Fine for a few
  tenants, painful at scale — that pain is the trigger to start Phase 12.
- Human error risk: wrong invoice or double click. Mitigated by idempotent mark-paid
  (`Idempotency-Key`) and a mandatory audit row for every billing action.
- Renewal is only as fast as the owner checks the bank account. The 7-day offline grace and
  the `past_due`/`grace_period` states absorb this delay for customers.

Follow-up work: FOP invoicing rules (numbering, PDF format, tax accounting) are an open
question for the accountant; email delivery of invoices is Later scope.
