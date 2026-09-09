# ADR 0003: Translation job ownership and workflow options

- Status: Accepted
- Date: 2026-09-08

## Context

Translation jobs belong to a tenant. An independent translator may submit a translation with only the translator signature, while work performed through a translation office requires both translator and office approval. Not every translation requires a notary. Acceptance requirements also vary by recipient and may be unspecified.

## Decision

- Translation Workflow owns translation jobs in its own `workflowdb` database.
- Tenant membership remains owned by Identity Access. Workflow forwards the caller bearer token to the Identity Access membership endpoint before reading or changing tenant data.
- A job created under an `IndependentTranslator` tenant receives the immutable `TranslatorOnly` signature policy.
- A job created under a `TranslationOffice` tenant receives the immutable `TranslatorAndTranslationOffice` signature policy.
- Notary requirement is explicit: `NotRequired`, `Required`, or `Undecided`.
- Notary processing mode is required only when notarization is required and can be `Physical`, `Digital`, or `Hybrid`.
- Acceptance profile is nullable. Null means no special acceptance profile. `Other` requires a free-text description.
- New jobs start in `Draft`; only draft details are editable.
- Creation publishes `TranslationJobCreatedV1` using the service's transactional outbox.

## Consequences

- A caller-supplied tenant id is never trusted without checking current membership.
- Identity Access availability is currently required for tenant-scoped workflow commands and queries. A signed cross-service identity token can replace this synchronous check later without changing the domain model.
- Signature requirements are captured when the job is created and cannot silently change if the tenant configuration changes later.
- Digital notary mode is represented but does not imply that a TNB integration is available; that capability remains isolated in Notary Integration.
