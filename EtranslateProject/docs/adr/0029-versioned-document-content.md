# ADR 0029: Persisted content descriptors and fail-closed version dispatch

Status: Accepted; migration generated and structurally tested, not applied to SQL Server in this step

Date: 2026-10-24

## Decision

Add nullable `ContentFormat` and `ContentSchemaVersion` to immutable draft revisions and template revisions. Keep `EditorContentJson` unwrapped and unchanged; do not store an editor vendor's native model as the application contract. Draft revision numbers, content schema versions, template pins and PDF renderer versions are separate concepts.

The shared `DocumentContentDescriptor` defines two read paths:

- `NULL / NULL`: unversioned legacy data. It may contain arbitrary previously permitted JSON and is not automatically relabelled as rich v1. Safe editing/printing can interpret only the existing allowlisted subset; unsupported structures remain opaque.
- `etranslate.rich-document / 1`: an explicit declaration of the current, unchanged application vocabulary. New declarations require its strict validator. The draft's plain-text projection is derived on the server rather than trusted from the request.

All other pairs are unsupported. Both fields are returned in revision responses and summaries. Write request fields are optional for backward compatibility. A client omitting both inherits the exact current revision descriptor; it cannot downgrade a rich revision to legacy. Unknown current metadata cannot be overridden by claiming v1 or omitting fields. A detached aggregate requires the exact saved current revision rather than assuming it was legacy. API queries remain constrained to the authorized document/template.

## Writes and preservation

The modern BFF explicitly declares rich v1 for rich/plain body saves and newly created simple templates. For a saved legacy body already representable by the editor, a user save creates a new strictly validated revision; it does not relabel or convert the original revision. Raw JSON fallback saves and edits of legacy templates preserve their original descriptor. No automatic bulk migration, flattening or unknown-field removal is introduced.

Applying a template copies that template revision's body descriptor. Changing a pinned template preserves the saved body descriptor, independently of the target letterhead descriptor. Review locks, optimistic concurrency, tenant/role/subscription checks and private asset ownership remain in place. New invalid metadata or invalid declared content is rejected before publishing events or saving.

The migration adds four nullable columns and two paired-metadata constraints, with no default v1 declaration, content UPDATE, archive rewrite or backfill. Existing rows remain NULL/NULL. Constraints allow both null, or a nonempty format with a positive version; they intentionally do not forbid a future release's valid format at the database level. Application dispatch still rejects unsupported versions.

## Reads, preview and PDF

The BFF uses persisted metadata to gate editing; unknown content retains its original JSON in read-only UI with a Turkish notice. The client independently checks metadata before mounting the provider, even if a stale page incorrectly claims compatibility. Body and template preview metadata are checked separately. Template settings reject unknown content before an upload or a new revision.

`DraftPdfInput` carries separate body and pinned-template descriptors. New PDF generation checks them before image parsing and again in deterministic HTML preparation. `DocumentContentDescriptor.TryRead` dispatches explicitly on format/version; unknown or future descriptors never fall back to today's parser just because the body looks familiar. Legacy header/footer doc roots retain the already-supported compatibility path.

For currently supported content, HTML, layout, fonts, Chromium settings, watermark and footer policy are unchanged. A unit test compares prepared HTML for legacy and declared-v1 inputs. The renderer byte-version constant is unchanged because rendering instructions did not change; this does not claim newly measured byte-for-byte PDF parity. This step creates no PDF artifacts. Existing cached PDFs and download/signing-preview paths remain tied to stored bytes/hashes, not regenerated from reinterpreted content.

## Deployment and rollback

Documents service already applies pending EF migrations at startup. This change does not start it or change that behavior. Do not start the updated service against a real database until backups and generated SQL have been reviewed. Deploy Documents API and BFF coherently; do not permit older API binaries to keep writing concurrently after introducing declared-format revisions. Additive schema compatibility with old rows does not mean old writers understand future declarations.

Down drops the new metadata columns. Do not use it as a routine rollback after versioned revisions have been written: it would discard declarations needed to interpret them safely. Preserve the database and use a compatible application release or a separately reviewed recovery plan.

## Verification and next work

See the [Persian guide](../testing/document-content-versioning.fa.md). Tests cover descriptor pairs, future versions, legacy preservation, immutable body/template snapshots, inherited metadata, detached aggregates, authoritative extraction and limits, BFF payloads and tampered modes, upload prevention, renderer dispatch and unchanged prepared HTML. SQL Server model/script tests run without a connection and assert no pending snapshot changes. Browser tests use synthetic loopback HTTP, not product APIs/SQL.

Actual SQL upgrade/rollback and full API/database end-to-end tests remain deployment checks. Live pagination, Word clipboard/DOCX fidelity and new formatting features are not added here. A richer schema must introduce a new version and matching validators, editor mapping and rendering, with explicit conversion/provenance/loss reporting; do not extend v1 in place. No conversion service or migration endpoint is implemented in this slice.

Browser stability caveat: one quiet repeat failed the existing save/reload case without retained diagnostic detail. A normal-verbosity rerun and three subsequent bounded repeats passed all three cases. Root cause is unconfirmed, not declared fixed. Retain TRX diagnostics and repeat in the deployment environment; this boundary's implementation is not a release sign-off.
