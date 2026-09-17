# ADR 0002: Tenant onboarding and subscription trial

- Status: Accepted; infrastructure details superseded by ADR 0005
- Date: 2026-09-01

## Context

An ETranslate customer is either an independent translator or a translation office. Both customer types need an isolated tenant, owner membership, and a 30-day trial. Identity and Billing must remain independently deployable and must not share a database.

## Decision

- Identity Access owns users, tenants, and tenant memberships.
- Billing owns subscriptions and trial state.
- Creating a tenant also creates an Owner membership for the authenticated user.
- Identity publishes `TenantCreatedV1` through a transactional outbox.
- Billing consumes that event with an inbox/outbox, creates at most one subscription per tenant, and publishes `TrialStartedV1`.
- The trial starts at the tenant creation timestamp carried by the event and ends exactly 30 days later.
- Identity and Billing use independently owned databases. Their current SQL Server implementation is defined by ADR 0005.
- The current event transport is defined by ADR 0005.
- MassTransit is pinned to the Apache-2.0 licensed 8.5.7 release. Moving to a later commercial major version requires a separate licensing decision.

## Consequences

- A temporary Billing outage does not roll back a successfully created tenant; the outbox delivers the event after recovery.
- Duplicate message delivery does not create duplicate subscriptions.
- Subscription authorization is not inferred from a caller-supplied tenant header. Public subscription access will be added through the authenticated gateway; the current read endpoint is internal.
- Local end-to-end development requires access to the configured SQL Server instance.
