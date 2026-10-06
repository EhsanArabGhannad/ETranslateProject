# 0011 - Version-bound internal document review

Status: Accepted for the development slice.

## Ownership and scope

Documents owns the review of its immutable draft/PDF artifact, its source-file snapshot, and the write lock. The decision is not an electronic signature, office seal, translator oath, notarization, or acceptance guarantee. Translation Workflow's job status remains `Draft`: a job-level orchestration projection/saga will be introduced with the later signing lifecycle. There is no cross-database transaction, direct access to another service's database, or claim of a completed signing workflow.

Each submission pins the current saved draft, its pinned template through an existing immutable PDF, the PDF ID/SHA-256 and the source-file IDs/names/sizes/hashes available at submission. The private archived PDF is checked for size/hash before submission. No automatic render or content flattening occurs in review endpoints. Rendering/archived PDF bytes remain unchanged after review.

The state sequence is `Draft -> AwaitingReview -> Approved | ChangesRequested | Withdrawn`. Awaiting/approved documents disallow draft saves, template application and source uploads at the domain/API layer. Returning for corrections unlocks the document but requires a newly saved draft before another submission. Withdrawal requires a reason and allows the same revision to be resubmitted as a new round. An approved document can be reopened by a reviewer with a reason (`Reopened`); the original approval actor/time/note are retained separately from the reopen actor/time/note. Resubmission requires a new saved revision. Old review rounds cannot decide a newer round.

## Authorization and consistency

Identity Access resolves active membership for every API request. Owner/Administrator can decide/reopen; Translator can edit and submit, and withdraw only their own pending submission; other roles are read-only. Independent-translator owners may record their own internal review. This slice does not enforce two different human actors or replace the future signature policy. Membership invitation, role management UI and assigned-reviewer queues are not implemented here.

Documents' SQL `rowversion` coordinates all document mutations, including uploads and decisions, while `(DocumentId, Round)` is unique. Source uploads always change the parent document timestamp (at least one tick), even with an identical/backward clock reading, so EF must update the parent and check its rowversion. Saves, review metadata and `DocumentReviewChangedV1` outbox events commit atomically in the Documents database. Concurrent losers return 409; their review/outbox changes roll back. Each event is an internal state change, never proof of signature. Consumers must tolerate duplicate delivery and order by round plus lifecycle transition, not arrival order alone. No consumer currently changes job status or sends review notifications.

Tenant, job, document and review/PDF IDs are bound together in all queries. BFF forms use antiforgery; downloads remain authenticated/private. Note text is limited to 2000 characters and rendered as encoded text. The UI blocks review submission while the translation body is dirty/saving and freezes body editing during an operation. Failed operations retain the body and note; the client reloads only after successful review operations. No-JavaScript forms reload; copy/save unsaved text first. Backend checks remain authoritative even for stale tabs or direct API calls.

## API

Under `/api/v1/tenants/{tenantId}/translation-jobs/{jobId}/documents/{documentId}`:

- `GET /reviews`: state, round, effective capabilities/current user and review history.
- `POST /reviews`: `{ expectedRevision, expectedRound, pdfVersionId }`, 201 on creation.
- `POST /reviews/{reviewId}/decision`: `{ action, note }`, actions `Approve`, `RequestChanges`, `Withdraw`, `Reopen`, 200 on success.
- 400 invalid action/note; 401 missing/expired bearer; 403 role/membership denial; 404 wrong document/PDF/review binding; 409 stale/locked state or missing new corrected revision; 503 missing/corrupt archived PDF. Unauthorized requests never disclose review content.

## Verification

Domain tests cover frozen writes, exact revision binding, lifecycle/replay rejection, correction/withdrawal/reopen behavior, audit preservation, reviewer roles and concurrency mapping. `Verify-DocumentReviews.ps1` exercises the live SQL-backed API/BFF, source snapshots, antiforgery, cross-tenant denial, independent-owner self-review, concurrent submissions/decisions and save-versus-submit races. It creates isolated development fixtures, not production records. Browser QA checks preservation of dirty text, dynamic PDF selection, submission, return and approval. No provider e-imza/mobil-imza or notary integration is claimed.
