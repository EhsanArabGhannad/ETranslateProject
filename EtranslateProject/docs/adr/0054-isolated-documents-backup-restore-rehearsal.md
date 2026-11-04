# ADR 0054: Rehearse SQL and blob recovery in an isolated environment

Date: 2026-11-04

Status: Implemented as an opt-in local rehearsal

## Scope and decision

Add a repeatable test/runbook for recovering the Documents database and its complete retained local blob tree. This is not a production backup service or scheduler. The fixture starts no API, bus or writer; its SQL and filesystem data remain quiescent during backup. Five synthetic asset kinds cover source, unused document image, archived-template image, previous-revision PDF and unsigned preview. PDF-labelled bytes are storage fixtures, not rendered documents or signatures.

The owned SQL host makes a native `BACKUP DATABASE ... WITH COPY_ONLY, CHECKSUM`, runs `RESTORE VERIFYONLY`, reads `FILELISTONLY` and restores with explicit `MOVE` into a fresh run-specific catalog and fresh files under the QA output. There is no `REPLACE`, reuse of a caller-selected catalog, restore into the source or manipulation of an existing application database. `DBCC CHECKDB` runs on the recovered catalog with MAXDOP 1. COPY_ONLY and restore relocation follow [Microsoft backup guidance](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/copy-only-backups-sql-server?view=sql-server-ver17) and [RESTORE arguments](https://learn.microsoft.com/en-us/sql/t-sql/statements/restore-statements-arguments-transact-sql?view=sql-server-ver15).

Copy the entire retained fixture blob tree with relative key, size and SHA-256 manifest. Validate that manifest before creating the recovery folder; use CreateNew for files and refuse an existing destination. Compare restored metadata/draft/review history to its pre-backup snapshot and run ADR 0053's audit against the restored database/root. A later source draft must not affect the recovered catalog. Simulated loss and same-size corruption are confined to recovered fixture files; restore them from verified backup bytes. A damaged backup copy is refused before a destination exists.

The host tracks six created catalogs and a seventh recovered catalog for guarded cleanup, including partial restore failure. Backups and manifests remain under ignored QA output; dropping the recovered database removes its MDF/LDF. Native SQL backup/restore can leave ordinary msdb history; the test does not purge shared system history or change SQL service permissions.

## Acceptance and limitations

The opt-in test has eight grouped checks, exercised by `Verify-StorageRestore.ps1`. See the [Persian runbook](../testing/storage-restore.fa.md) for results. The first run exceeded the 90-second CHECKDB command budget; its owned catalogs were removed. The follow-up limits CHECKDB parallelism to one worker to avoid unnecessary local resource pressure.

This verifies a quiesced Documents recovery on one local Windows SQL instance. It does not test multi-service consistency, live backups during uploads/outbox delivery, log/PITR recovery, offsite media, encryption/key recovery, RPO/RTO, data-protection keys, real signed PDFs or disaster recovery of the complete SaaS. A full SQL backup alone does not include external blob files. Production recovery requires a separately reviewed coordinated backup and restart policy for all service databases, messaging state, storage and keys; no production commands are automated by this rehearsal.

Follow-up: [ADR 0055](0055-patched-transitive-cryptography-and-messagepack.md) patches the known transitive package warnings observed during the rehearsal build.
