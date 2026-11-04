# ADR 0050: Recoverable source and body-image uploads

Date: 2026-11-02

Status: Accepted for the bounded upload recovery/concurrency slice

## Decision

Use an optional `uploadId` query parameter on document source/image uploads as the immutable asset ID. The web client generates one UUID for the selected `File` object and retains it for manual retries in the current page. Existing callers can omit it and keep server-generated IDs. The existing asset primary keys enforce uniqueness; no migration, receipt table, content-schema change or renderer change is required.

Authorization and document ownership checks precede replay. Reusing an ID requires the same document, uploader, normalized filename/content type, length and SHA-256. The fingerprint is streamed with the existing size bound. Changed payload/scope returns 409 without returning another document's metadata. The document must still allow writes; image replay also checks the expected draft revision. Replays after review lock are rejected. A matching committed asset returns 200; a new upload returns 201. Replaying does not save the tracked parent, add a draft or publish another upload event.

Every concurrent attempt writes a distinct physical blob key, even when its logical asset ID matches. A primary-key/rowversion conflict removes only that attempt's blob, then checks whether the same upload already committed. This prevents a losing contender from overwriting or deleting the winning blob. Image dimensions, file signatures, size limits, private asset lookups and historical retention remain in force.

The web controllers forward the optional ID. Sources now use the shared 45-second fetch/body deadline, reject overlapping submissions and deduplicate acknowledged metadata in the visible list. During a source upload, body editing, draft save and review are temporarily frozen; their prior permitted state returns on transient failure. The existing image-upload deadline also covers private-image decode. Confirmed 402/403/409 upload denials keep editing locked while retaining local content. Transport/timeout/invalid-JSON and 5xx outcomes use Turkish unconfirmed-outcome feedback; no automatic retry is added.

Source preview is separate from upload acknowledgement. Once metadata arrives, controls are restored even if the PDF preview is still pending. A failed private body-image decode can retry the same upload and insert the existing asset once.

## Acceptance

`Verify-EditorUploads.ps1` builds the client/solution, runs focused browser regressions and starts six real services with six fresh owned SQL databases. Twelve real-product cases cover offline source upload, response loss after source/image commit, image decode failure, a pending source deadline and blocked duplicate/save/review/image submissions, concurrent identical source/image requests, payload/scope/anonymous denial, and four concurrent upload-versus-save/review HTTP pairs.

The four HTTP pairs may both succeed in a valid serial order; they do not force both handlers to read the same SQL rowversion. If source upload and review both succeed, the review snapshot must include that source. A successful review rejects further uploads. Drafts stay unchanged except an accepted save, and exact source/image/PDF metadata counts, physical blob counts and hashes are checked. No leftover `.uploading` file is permitted in the successful run.

Additional browser tests advance the browser clock to cover both 45-second upload deadlines, require the same ID on retry, exercise all six source/image 402/403/409 combinations, and keep writes usable while an acknowledged PDF's preview is pending. A storage cancellation test aborts after a partial write and requires the temporary file to disappear without publishing a final blob.

See the [Persian guide](../testing/editor-upload-recovery.fa.md) for results and manual recovery.

## Limits and deployment

The identity is page memory, not durable offline storage. Reload/closure, reselecting a file or opening another page starts a new operation; identical bytes uploaded under a new ID are intentionally allowed. The UI says to retry the current selection. Old clients without an ID retain their old duplicate-upload behavior. Deploy the Documents API before the updated Web/bundle; older API instances cannot provide this replay guarantee during mixed-version routing.

This acceptance does not prove recovery from SQL/storage failure at an uncertain commit, service/process crash, browser crash, shared/object storage outages or a full HTTP body disconnect matrix. Existing general exception/cleanup paths are not redesigned here. Unreferenced managed images remain retained under the existing history policy; no garbage collector or asset picker is introduced. Roadmap step 7 remains incomplete.

Follow-up: [ADR 0051](0051-editor-large-document-performance.md) adds local large-document measurements and avoids repeated pagination measurement for view-only state changes.

The general cleanup limitation above is addressed separately by [ADR 0052](0052-blob-retention-after-uncertain-database-commit.md): unknown persistence outcomes retain blobs; explicit conflicts check references before deletion. Process crash and durable orphan reconciliation remain separate.
