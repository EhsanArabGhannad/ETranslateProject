# ADR 0016: Secure read-only subscription overview

Status: Accepted for the development slice

Date: 2026-10-11

## Decision

Billing remains the sole owner of subscription persistence and existing event-driven 30-day trial creation. This slice adds observation, not billing mutations or access enforcement. No schema migration, checkout, pricing, renewal, paid activation, seats, invoice or subscription entitlement check is introduced.

`GET /api/v1/tenants/{tenantId}/subscription` forwards the caller's bearer to Identity Access's tenant-access endpoint before querying Billing's database. Owner, Administrator, Translator and Reviewer must be active members of exactly that tenant. Identity verifies the token and current membership, not a caller-supplied tenant header or cached role. The returned actor is also checked for tenant, nonempty user ID, recognized tenant type and role. No authentication returns 401; unauthorized membership or unknown tenant returns 403; failed/untrustworthy Identity responses return 503. Caller cancellation is propagated. The legacy `/internal/v1/subscriptions/{tenantId}` route uses the identical handler and is no longer anonymously readable. Existing callers must supply an authorized bearer; future machine-to-machine access needs a separate, explicit policy.

The response contains tenant ID, nullable plan code, status, trial UTC dates, observation UTC time and nullable remaining trial days. An existing subscription returns 200. An authorized tenant without a Billing row returns 202/Pending with no invented plan, dates or days, because onboarding is asynchronous. GET does not insert a row, extend a trial or persist expiry. It uses the domain's effective status at one clock reading, so the exact expiry boundary is correct even before the existing worker persists Expired. Trial duration stays exactly 30 days; remaining days are rounded up and bounded to 0..30, with zero after expiry. A nontrial plan has no trial-days claim. Pending and dependency failure are distinct states.

Web exposes an authenticated, tenant-scoped status page linked from the workspace. It first reads the authorized tenant from Identity, then calls Billing with the server-held token. The BFF validates the response's scope, status, trial duration, calculated days and 200/202 consistency; malformed data produces 502 instead of a misleading subscription status. Billing unavailability produces 503 and a clear unknown-state message. Authentication/authorization errors retain the existing BFF behavior and are not disguised as a trial. User input and tenant names remain HTML-encoded; bearer tokens are never rendered. API and page responses are private/no-store; the page reapplies the cache policy at response start so layout antiforgery generation cannot replace it.

The page labels timestamps as Istanbul UTC+03:00 and exposes their ISO UTC instants in semantic time elements. This is an explicit presentation offset, not a dependency on the host's locale or timezone. It shows the observation time and has manual GET refresh only. The displayed day count is approximate rounding; the exact end timestamp is authoritative. Subscription status applies to the whole tenant, not each member account.

AppHost supplies Billing's Identity service reference and Web's Billing reference. Web intentionally does not wait for Billing startup: subscription errors must not by themselves prevent normal workspace access. Production service discovery and secure deployment remain separate work.

## Limits

These are live checks, not distributed revocation locks; an operation already authorized may finish if membership changes immediately after its check. There is no write API on this page, no automatic subscription extension, and no expired-trial restriction. Existing translations and document history remain unaffected. A plan code or Active status is not a claim that a payment was processed.

Database failures are not Pending and must not be inferred as a free trial. Pending is shown only after successful tenant authorization and a successful database read with no row. Production readiness still requires machine identities, secure transport, monitoring, audit/abuse limits, recovery, retention, provider/payment integration and a separately designed enforcement policy. The existing shared messaging schema's startup DDL can race on a cold/concurrent service start; this slice does not redesign transport initialization.

## Verification

Unit tests cover trial rounding and exact boundaries without state mutation, pending scope, all eligible roles, identity failures/malformed data/cancellation, BFF validation, unavailable-state pages and explicit timestamp formatting. `Verify-Subscriptions.ps1` runs Web/team baseline checks and live authentication, active membership, role changes, cross-tenant isolation, both API aliases, unsupported mutation methods, private caching, trial dates, refresh and BFF access checks. Optional expired-fixture setup modifies only the synthetic independent tenant created in that run and verifies that workflow writes are still permitted, since enforcement is outside this slice. Fixtures are retained for local inspection. Read-only rendered trial/expired HTML can be saved for native browser visual QA without exercising account-security UI.
