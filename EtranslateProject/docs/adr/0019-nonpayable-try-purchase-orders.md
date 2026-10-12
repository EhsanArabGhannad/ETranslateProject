# ADR 0019: Nonpayable TRY subscription purchase/renewal drafts

Status: Accepted for the development slice

Date: 2026-10-12

Extends ADR 0018. The user selected Turkish lira, but has not selected a gateway or subscription prices. This is not payment integration or a production checkout.

## Decision

Billing owns nonpayable `SubscriptionPurchaseOrder` snapshots in its own database. Currency is fixed to `TRY`, not a client field. Each eligible plan has optional server-configured `PriceTry`: null means unknown, never zero/free. A configured amount must be positive, fit SQL decimal(18,2), and have at most two decimal places. It describes the total for that profile's configured 1..12 calendar months, not a seat price or a tax invoice. Defaults remain null; no commercial price is invented. Configuration changes require restart. Even a priced draft has `checkoutEnabled=false`, `paymentReceived=false`; the catalog always has `purchaseEnabled=false`. `pricingConfigured` only reports whether any eligible enabled profile has an amount, not readiness to charge.

The catalog now uses the authoritative tenant type returned by Identity on that request, not Billing's optional historical type. Thus legacy unknown-type records can see eligible profiles without guessing or a GET-side backfill. New orders reject a known Billing/Identity type mismatch. Creating an order does not reconcile the legacy subscription type: internal paid allocation remains blocked until a future trusted reconciliation, as in ADR 0018.

Only currently active Owner/Administrator members can list, read, create or cancel orders. Identity membership/role is checked on every request using server-forwarded bearer credentials; client tenant/role headers are not trusted, dependency failure denies access, and responses are private/no-store. Other active members can still read their subscription and catalog. An expired, canceled or past-due subscription can hold a draft because preparing a future purchase is separate from business write access; no draft grants access or reactivates anything. A missing asynchronously provisioned subscription yields 503/`subscription_pending` without creating one or a trial.

Create accepts only plan code and a nonempty caller nonce. Amount, currency, months, actor, payment reference and state are not writable; unknown JSON fields are rejected. Billing snapshots the enabled eligible profile's code/name/months/price, verified tenant type, creator, subscription, UTC creation/deadline and purchase/renewal intent. Intent is a label based on valid paid history, not an entitlement action. No call to `PaidPeriodApplicationService`, provider API, webhook, outbox debit or card collection exists.

The unique `(TenantId, RequestId)` key and dedicated fresh EF contexts protect concurrent retries. Replays are bound to the original creator and exact plan code. Lookup precedes current catalog validation: a matching retry returns the original immutable amount, dates and status even after configuration changes, cancellation or expiry. A different creator/plan yields 409; the same nonce in another tenant has independent scope. Replaying never extends the deadline. Different nonces can produce multiple open drafts; this slice does not claim rate limits, quotas or one-open-order semantics.

Draft lifetime is exactly 30 minutes. Status is Draft, Canceled or effective Expired only, never Paid. At exact deadline, a stored Draft is observed as Expired without a database write, worker, deletion or deadline extension. Cancel is manager-only, tenant-scoped, and guarded by an expected version. Expired/stale requests yield 409. Repeating an already completed cancellation is a no-op; cancellation affects only the order, not the subscription. An EF concurrency race yields 409 for caller refresh. The restrictive subscription FK preserves order history.

The Web BFF shows eligible plans and the latest 50 orders, with UTC timestamps displayed in Istanbul time. A fresh page issues a fresh nonce; form retries use the same nonce. Forms require antiforgery; bearer tokens remain server-side. BFF validates tenant/shape/currency/time/state and rejects upstream payment/checkout claims. The page explicitly says no money is received, no subscription is activated/renewed/canceled, and this is not a receipt or invoice. No checkout button or public activation/settlement endpoint is added.

## Persistence and verification

`AddSubscriptionPurchaseOrders` adds only `billing.purchase_orders` with decimal price, restrictive subscription FK, unique tenant/nonce, tenant/creation listing index and optimistic version. No existing trial, paid period or document is rewritten. Down drops order history and is not a data-safe rollback.

Unit/controller tests cover role boundaries, price validation, immutable quotes, exact expiry, replay identity, cancellation and strict contracts. Opt-in native SQL tests cover concurrent retries, configuration changes, creator/tenant binding, cancellation replays, and missing/ineligible subscription cases. The local HTTP script includes previous signing/subscription/paid-period regressions and tests API/BFF isolation, forged fields, current roles, concurrent retries, expiry, CSRF and unchanged subscription dates/access. Its clock simulation changes only a new synthetic order's dates; fixtures are retained locally. These are functional/HTML checks, not native browser visual QA or verified payments.

## Future payment boundary

Gateway and commercial prices still need user decisions. A future adapter must verify server-side settlement authenticity and compare immutable order tenant/amount/TRY/months/plan with current authoritative eligibility before any paid allocation. Never trust browser redirects, a supplied payment reference or a Paid field. Settlement-versus-expiry races, machine authentication, callbacks, idempotent payment attempts, expired quotes, pricing/tax changes, invoice policy, retries/reconciliation, refunds/disputes, rate limits and production deployment need separate designs. Existing drafts must not silently become payable: issue/validate an explicit new payable order under the chosen provider policy.
