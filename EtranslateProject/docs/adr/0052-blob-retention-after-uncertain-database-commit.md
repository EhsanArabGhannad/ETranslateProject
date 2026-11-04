# ADR 0052: Retain blobs when database commit outcome is uncertain

Date: 2026-11-04

Status: Accepted for bounded endpoint fault recovery

## Problem

Source uploads, document images, template assets, draft PDFs and unsigned signing previews write bytes before saving metadata in SQL Server. Their general exception handlers previously deleted the new blob. A successful commit followed by a connection error or cancellation can therefore leave committed metadata pointing to deleted content. A retry can also report a duplicate or concurrency conflict after its first attempt committed. SQL and the local filesystem do not share an atomic transaction. See Microsoft's [commit failure and idempotency guidance](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency#transaction-commit-failure-and-the-idempotency-issue).

## Decision

All five paths now use a pending-blob lifetime, started before calling the store. Before metadata persistence starts, an unsuccessful operation discards its own generated key. Immediately before `SaveChangesAsync`, the lifetime records that persistence was attempted. A confirmed successful save completes it. Any other general failure after that boundary retains the file and logs its internal storage key for reconciliation. A later negative database query cannot prove rollback of an uncertain write, so the general path does not use one to justify deletion.

The existing explicit SQL unique/concurrency conflict paths check references to the exact attempt key before discarding it. The probe uses a fresh DI scope and DbContext, a separate five-second cancellation budget and command timeout. It covers source files, document images, template assets, draft PDFs and signing previews, including retained historical metadata. A referenced key is kept; an unreferenced losing attempt can be discarded. If verification fails, the blob is retained. This also protects a committed first attempt when a retry reports a duplicate. Request-specific upload replay and PDF lookup continue to control the HTTP response and authorization.

Cleanup uses an independent cancellation budget. A deletion failure is logged and never replaces the original error, validation rejection or conflict. Ordinary success still needs no reference probe. Authorization, subscription checks, upload identity scoping, renderer v20, schema v10, migrations, dependencies and archived bytes remain unchanged. No request retries, reconciliation worker or automatic orphan deletion are added.

## Acceptance

`Verify-StorageFailures.ps1` runs eight lifetime checks and a 32-case HTTP suite against production endpoint mappings, real SQL Server migrations/provider/outbox and the real local blob store. Six new owned QA catalogs are created and removed; only the documents catalog is used by this in-process harness. Identity, Billing, Trust and PDF rendering are explicit test doubles. The transport is in-memory in the harness, with bus-outbox delivery disabled so the SQL rows can be counted; production remains SQL transport.

For each of the five asset kinds the suite injects failures before file publication, after file publication but before SQL, before SQL writes, after a real committed save, cancellation after a real committed save, and a duplicate exception after a real committed save. The last exception wraps an actual SQL 2627 from a separate deliberately duplicated fixture row, simulating the error shape of a retry; it is not a real network-induced execution-strategy retry. Two additional source/image cases update the parent through an independent SQL connection and verify that a real stale-rowversion failure discards only the losing blob.

The suite downloads and hashes every committed asset, injects and recovers a storage read failure, checks all five reference-probe branches, preserves drafts/review state, and verifies the expected SQL outbox message count for accepted uploads. Source/image and PDF/preview retries reuse the confirmed asset. Template uploads do not have a client operation identity; recovery checks the asset list rather than blindly uploading again. Five deliberately retained orphan files from the pre-SQL exception cases are expected. No `.uploading` files or missing referenced files are permitted.

The existing upload acceptance is rerun separately with six real services and new SQL databases. Results and operator guidance are in the [Persian guide](../testing/storage-failure-recovery.fa.md).

## Limits and follow-up

This is fault injection around real SQL/file operations, not a stopped SQL instance, severed SQL connection, full disk, OS permission outage, process kill, power loss, object-storage outage or proof of filesystem durability. It does not change missing-file responses or repair previously lost content. An ambiguous request may leave an unreferenced file, deliberately preferring retention to data loss. Logs are diagnostic signals, not a durable reconciliation queue. Template re-upload and clients without an upload identity can still duplicate assets.

Next: a read-only inventory of referenced, missing and unreferenced managed files with hashes and operation age. Deletion requires a separate policy accounting for in-flight writes and historical references. Roadmap step 7 and deployment acceptance remain incomplete.
