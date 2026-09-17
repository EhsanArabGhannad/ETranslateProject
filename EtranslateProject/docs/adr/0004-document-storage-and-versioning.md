# ADR 0004: Document storage and immutable draft revisions

## Status

Accepted

## Context

Translation source files are legal evidence and must remain byte-for-byte traceable. Editor content changes frequently and concurrent saves must not silently overwrite another translator's work. The Documents service must also remain deployable independently from Translation Workflow and Identity Access.

## Decision

- Documents owns a separate PostgreSQL database named `documentsdb`.
- A translation job has at most one translation document in the first product phase.
- Source files are immutable, limited to 25 MiB, and restricted to PDF, JPEG, PNG, and TIFF. Their binary signatures must match the declared media type.
- Every stored source file has a SHA-256 digest persisted with its metadata.
- Binary content is accessed through `IDocumentBlobStore`. Local development uses a filesystem provider; a shared object-storage provider can replace it without changing the domain or HTTP contracts.
- Editor content is stored as immutable JSON revisions. A client must send `ExpectedCurrentRevision`; a stale value returns HTTP 409 instead of overwriting newer work.
- Documents verifies tenant membership through Identity Access. Creation additionally verifies the translation job through Translation Workflow. Every later query scopes the document by tenant and translation-job identifiers.
- Integration events are written with the Documents database transaction by the MassTransit transactional outbox.

## Consequences

- Source files can be independently verified using their SHA-256 digest.
- Draft history is auditable and supports future diff, approval, and signature workflows.
- Production deployment must configure an object-storage implementation before horizontal scaling; the local filesystem provider is intended for development only.
- The one-document-per-job rule can be relaxed later with a migration if multi-document jobs become a product requirement.
