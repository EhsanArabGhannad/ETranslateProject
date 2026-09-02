# ADR 0001: Distributed monorepo with coarse-grained services

- Status: Accepted
- Date: 2026-09-01

## Context

ETranslate must support independent translators, translation offices, qualified electronic signatures, subscription billing, and future integration with Türkiye Noterler Birliği systems. The notary integration contract and operational model can evolve independently of the translation workflow.

## Decision

The system will be implemented as a distributed monorepo containing coarse-grained, independently deployable services.

Each service:

- owns its data and migrations;
- exposes versioned contracts;
- never reads another service's database;
- publishes integration events through an outbox;
- handles incoming messages idempotently;
- carries tenant context on tenant-scoped operations.

TNBBS-specific models and protocols are isolated inside the Notary Integration service. The core workflow communicates with that service through stable internal commands and events.

## Consequences

Local development requires orchestration, service discovery, observability, and contract testing. Aspire provides the initial development experience. Distributed transactions are avoided; eventual consistency and explicit workflow states are used across service boundaries.
