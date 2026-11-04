# ADR 0053: Read-only inventory of managed storage and retained SQL references

Date: 2026-11-04

Status: Accepted for the local administrator inventory slice

## Decision

Add `ETranslate.StorageAudit`, a standalone .NET 10 console tool. It explicitly receives a connection environment variable name, an existing absolute storage root and a new JSON report path outside that root. It does not start an API, bus, AppHost, migration or background job. The tool references the Documents assembly for the existing EF model; no service references the tool. Production endpoints and storage behavior are unchanged.

Read all retained source-file, document-image, template-asset, draft-PDF and signing-preview rows with one bounded `UNION ALL` projection and no tracking. Do not filter on latest revisions, template activation, current body image references or current renderer version. Group references to the same local key, preserving all owning metadata and recording conflicting size/hash declarations. A SELECT-only SQL principal is the intended operational credential; `ApplicationIntent=ReadOnly` is descriptive and does not replace SQL permissions.

Enumerate local storage without following observed reparse points. Validate metadata paths, reject root/parent traversal, alternate streams, Windows device names and ambiguous trailing dots/spaces. Refuse a linked storage root or report path. Compare file length and streamed SHA-256 with expected metadata; record filesystem last-write time/age separately from the metadata recorded time. A missing path, unreadable file, mismatch and changing file are distinct outcomes. Unreferenced/temporary entries are separated by a configurable age window, default 24 hours; temporary `.uploading` content is never opened or hashed.

Defaults cap filesystem entries and metadata references at 100,000 each, individual file hashing at 25 MiB, total hash reservations at 1 GiB and cooperative cancellation at 300 seconds. A limit, inaccessible directory, skipped link, invalid key or unstable read makes the report incomplete. When metadata selection is incomplete, an unmatched physical file is `ReferenceUnknown`, never asserted to be unreferenced. A known missing/mismatched/unreferenced file is a finding even when the observation completed successfully.

Report version 1 includes observation times, selected server/catalog/root, per-key references, actual length/hash/time, statuses and counts, `readOnly=true`, `authorizesDeletion=false` and `consistency=NonAtomicObservation`. It excludes connection strings, exception messages, original-filename fields and document text. Internal keys/IDs and physical relative names remain sensitive administrator data. Report creation uses `CreateNew`; an existing report cannot be overwritten. Failure/cancellation during serialization can leave invalid partial JSON; only a valid report paired with the reported exit result is confirmed.

Exit codes are 0 complete/no findings, 1 complete/with findings, 2 incomplete, 3 failed/configuration error and 130 cancelled/timeout. The tool has no repair, deletion, quarantine, signing, scheduler or tenant-facing route. An old/unreferenced key is never authorization to delete content.

## Acceptance

`Verify-StorageInventory.ps1` builds the solution and runs 28 scanner/command cases plus one opt-in SQL/console acceptance with eight grouped checks. The latter creates six fresh owned QA catalogs; only the documents catalog is used and migrated. It seeds eight asset rows across all five kinds, including a previous-revision PDF, an unused retained document image and an archived-template asset. The sample inventory has five verified keys, one missing key, one size mismatch, one same-length hash mismatch and four unreferenced/temporary age cases.

The metadata projection is executed under a database user with SELECT permission and explicit INSERT/UPDATE/DELETE denial. The real console executable then writes an inventory, refuses overwrite and returns an incomplete report when the metadata cap is reached. A real Windows junction to another owned fixture directory is skipped; linked-root input is rejected. File contents, lengths and last-write timestamps, as well as retained metadata/drafts/reviews, match their pre-scan snapshots. All six catalogs are removed. Data is synthetic; no application database or user document is inspected.

See the [Persian guide](../testing/storage-inventory.fa.md) for the sample, usage, meanings and verification results.

## Limits and follow-up

This is an observation across SQL and a live filesystem, not an atomic backup snapshot or durable reconciliation queue. Uploads can finish before/after either read; filesystem age is not proof of operation age, inactivity or ownership. Observed reparse points are excluded, but the tool is intended for an operator-controlled local root, not a security boundary against hostile concurrent path replacement. Windows local SQL/filesystem behavior is tested; object stores and other platforms are not accepted here.

Hash equality verifies bytes against recorded metadata, not document validity, legal acceptance or an electronic signature. The tool does not repair losses predating ADR 0052 and cannot establish that an unreferenced file is disposable. Backup/restore acceptance and any retention/deletion policy remain separate work. Roadmap step 7 remains incomplete.
