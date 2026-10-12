# ADR 0017: Fail-closed subscription enforcement on business mutations

Status: Accepted for the development slice

Date: 2026-10-11

Update: ADR 0018 now requires bounded paid dates for Active and adds internal idempotent allocation; statements about missing paid dates below describe the original slice.

Supersedes ADR 0016's lack of enforcement, not its secure read-only overview.

## Decision

Billing owns the subscription decision as well as its database. A new authenticated, live-membership-checked `GET /api/v1/tenants/{tenantId}/subscription/write-access` exposes the versioned `SubscriptionWriteAccessV1` observation: tenant, effective status, write grant, safe reason, observation UTC time and trial end UTC time. The observation itself always returns 200 when successfully authorized/read, even when no write is granted. It is not an authorization token, paid receipt, create/renew command or substitute for role checks.

Valid Trialing and trusted Active states can write. At the exact trial end, effective status is Expired without waiting for worker persistence. Expired, PastDue and Canceled require a valid subscription. A missing row is Pending, never a free trial or expiry. Reads do not alter state or trial dates. No public activation/extension endpoint, payment integration, price, seat quota, grace period or database migration is added. Active is a trusted Billing state; this slice provides no path for a user to manufacture it.

`ETranslate.SubscriptionAccess` is a shared HTTP integration building block, not a shared database/domain implementation. It forwards the server-side user bearer to Billing for each mutation. Billing independently verifies membership with Identity. The client validates tenant, known status/reason/boolean combinations and timestamps. Observations older than 30 seconds or over five seconds in the future are rejected; these are validation limits, not cache TTLs. A trial expiring between observation and receipt is denied at the consumer's clock. No positive/negative grant cache is used. The entire HTTP GET, including inherited safe-read resilience, has a four-second timeout. HTTP failures, malformed/unsupported JSON, mismatched/stale responses and open circuits fail closed; caller cancellation propagates.

The shared endpoint filter applies to whole business route groups: Workflow jobs and assignment, Documents documents and templates, and Trust signing preparations. POST, PUT, PATCH, DELETE and future non-read methods are gated before their business handlers. GET, HEAD and OPTIONS skip Billing entirely. Missing route scope is an internal error, never taken from `X-Tenant-Id`. ASP.NET request binding/size/type checks may reject a malformed request before its filter; no business mutation is then executed. Role checks remain in handlers, so a grant does not confer writing authority. Legitimate expired members may see the subscription denial before a handler's role denial; outsiders/inactive members are rejected by Billing's membership check.

Protected mutations return ProblemDetails 402 with `subscription_required`, 503 with `subscription_pending` or `subscription_unavailable`, or normal 401/403 for authentication/membership rejection. They are private/no-store. These are safe machine codes, not arbitrary upstream display strings. 402 does not imply an available checkout or a processed payment. There is no fail-open mode or request replay after ambiguous write failure.

Read/download/history APIs retain their existing membership checks but do not consult Billing. Identity login, tenant/team management and subscription overview are not gated. No document is deleted on expiry. PDF/preview creation or reuse via POST is blocked; archived downloads via GET remain available. Review decisions, assignment changes, template updates, uploads and proposal cancellation are all mutations and are blocked, including manager actions.

Web uses the same client only to present a bounded read-only status, without making Billing failure an error for workspace reads. Existing saved content/history/download links remain available. Forms are disabled/hidden and editor editability incorporates subscription status; this is presentation only. API enforcement still catches a page left open until expiry or a manually crafted request. The BFF preserves whitelisted subscription codes/messages, including JSON PDF/upload/review responses, and retains rejected draft text for copying with the upstream 402/503 status. It never displays a fake save success or renders tokens. Restoring a trusted valid subscription is observed on subsequent checks without restarting services.

AppHost gives Workflow, Documents and Trust runtime Billing references with no startup WaitFor. Billing waits only for Identity; this introduces no startup cycle or Billing dependency for read handlers. Every service continues to own its own data.

## Limits

Authorization occurs before each business handler; it is not a distributed revocation lock or an atomic transaction with Billing. Work already authorized may complete if expiry, cancellation or membership change occurs immediately afterward, including a long render. Future payment events need verified provider results, idempotency, auditing, and properly modeled paid periods before granting Active. No paid-period dates exist yet; Active expiry is therefore future Billing work. Clock synchronization, transport/machine identity hardening, rate limits, trial abuse prevention, recovery and production deployment remain separate work. Users can still create separate workspaces under the existing onboarding policy; enforcing a per-account/legal-entity trial limit needs its own design.

## Verification

Unit tests cover exact expiry, no mutation, missing state, recognized statuses, stale/scope/coherence failures, timeouts/cancellation, uncached checks, safe read bypass, absent route scope, nonexecution on rejection and distinct problem codes. BFF tests cover readable workspaces, fail-closed UI defaults, safe messages, preserved text and 402/503 statuses. Live verification runs signing-preview/Web/team regressions, then all current mutation paths including multipart uploads, direct API and BFF requests, no stored artifact/metadata changes, byte-identical retained downloads, account/team availability, role/membership isolation, independent tenants and synthetic status restoration/re-expiry.

The fixture script uses only loopback service URLs and changes only the two newly-created synthetic trial tenants in the configured native development SQL instance. Synthetic Active/restored-trial states are test setup, not product payment or renewal capabilities. Test records remain for inspection; nothing is deleted. Fixture setup waits for asynchronous subscription provisioning by retrying only its GET, never a business mutation. Dependency failure and Pending are also covered deterministically with in-memory HTTP substitutes.
