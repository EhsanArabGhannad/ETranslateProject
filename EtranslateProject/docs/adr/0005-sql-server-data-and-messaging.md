# ADR 0005: SQL Server for service data and asynchronous messaging

- Status: Accepted
- Date: 2026-09-17

## Context

The initial local architecture used PostgreSQL databases and RabbitMQ containers. That required Docker Desktop or another container runtime and, on Windows, commonly depended on WSL 2. The development environment already provides SQL Server and the project should run natively on Windows without Docker or WSL while preserving microservice boundaries and asynchronous communication.

## Decision

- Use one SQL Server instance as the local database engine.
- Keep database-per-service ownership. Identity Access, Billing, Translation Workflow, and Documents own `ETranslateIdentity`, `ETranslateBilling`, `ETranslateWorkflow`, and `ETranslateDocuments` respectively.
- No service may query or write another service's database.
- Use the MassTransit SQL Server transport backed by the separate `ETranslateMessaging` database for queues and topics.
- Keep MassTransit transactional outboxes in each service database so domain changes and outgoing messages are committed atomically.
- Apply committed EF Core SQL Server migrations at service startup. For local orchestration, AppHost creates the messaging database if it does not exist; MassTransit's migration hosted service then initializes its schema without attempting to manage SQL logins.
- Use Windows Authentication for the checked-in local development configuration. Credentials and production connection strings must be supplied by the deployment environment and must not be committed.
- Keep integration-event contracts transport-neutral so another broker can replace the SQL transport without changing domain behavior.

## Consequences

- Local development no longer requires Docker, WSL, PostgreSQL, or RabbitMQ.
- Service data remains logically and operationally separated even though the databases share one local SQL Server instance.
- SQL Server is both the data engine and local message broker, reducing development setup complexity.
- A failure of the shared SQL Server instance affects all local service databases and messaging. Production may use separate SQL Server instances or a dedicated broker when availability, throughput, or isolation requirements justify it.
- The production SQL Server edition and licensing must be selected separately; Developer Edition is not licensed for production use.
