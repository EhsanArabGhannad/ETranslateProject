# ADR 0047: Multi-tab save, template and review conflict acceptance

Date: 2026-10-30

Status: Accepted for the bounded real-service/SQL concurrency slice

## Decision

Retain optimistic revision checks, immutable draft/PDF records and SQL rowversion enforcement. Add an isolated acceptance suite for stale tabs and competing operations. No automatic merge, collaborative editing, background tab synchronization, content-schema change, renderer change, migration or dependency is introduced.

The existing save handler already locks on 409 and preserves its local source. The review handler previously restored editing/review buttons after a confirmed conflict. On a JSON review response with 409, 402 or 403, it now retains the source and keeps mutation controls disabled. Page navigation/zoom remain available. A transient failure retains the prior editable state and prior button states, so a manager viewing an already-locked document can still retry a review action after 503. The denial flag is scoped to the current request; it must not be inferred from a document that was read-only before the request.

Server authorization, subscription and document-state checks remain authoritative. This client correction improves recovery feedback and does not replace server concurrency control. The user must inspect a fresh current version and deliberately reapply needed edits; the rejected request is not automatically retried with a new expected revision.

## Acceptance layers

`Verify-EditorConcurrency.ps1` starts the same six product services against six new run-specific SQL databases. Its output suite name is allowlisted in `IsolatedSqlProductHost`. The test uses one registered synthetic owner, one workspace and separate tabs in an independent Chromium context. It does not run the legacy optional SQL tests that target an existing database.

Sixteen scenarios are recorded:

- Nine browser/service cases: competing saves, both ordered interleavings of save/template, save/review and template/review, duplicate review submissions and competing approve/return decisions. Requests are held before forwarding in the browser, then released in a chosen order or together. Production responses are not fabricated. The test does not claim that an ordered case runs two server transactions at the same instant.
- Three pairs of concurrent document API requests: save/template, save/review and template/review. Exactly one succeeds and the other returns 409; the winner is not predetermined.
- Four real SQL stale-snapshot cases: two EF contexts load the same parent rowversion before either commits, then alternate the first commit for save/template and save/review. The stale context must fail and roll back its insert. These cases exercise persistence directly, not the HTTP/outbox path.

Each scenario begins with a v10 draft, a private body image, a source attachment and an actual v20 draft PDF. Assertions check the exact winning body/pin, revision count and author, the original immutable body/pin, review round/state/reference, source snapshot, and image/source/PDF counts. Blob paths and SHA-256 values must remain unchanged by the competing operations. A retry carrying the old expected revision cannot create another draft.

Rejected browser saves retain their exact local JSON and become read-only. Rejected review operations also disable review buttons. Changing native page navigation after a conflict cannot change that local JSON or unlock it. Template conflicts use the existing web redirect/notice; the API still returns 409, and no template revision is silently pinned. The two 302 responses in a save-first/template-second browser case therefore do not mean both mutations succeeded.

## Verification and limits

The [Persian guide](../testing/editor-concurrency.fa.md) records the executed run and manual recovery steps. An additional production-bundle browser regression covers review 409/402/403 plus editable and initially-read-only 503 retries. No real user documents, payment, external requests, AppHost or pre-existing databases are used.

This adds bounded race coverage to roadmap step 7. It is not a load/soak test, a full multiple-user/browser matrix, an upload-versus-review race matrix, a transport retry/outage audit or deployment verification. Real-time collaborative editing remains a separate feature.

Follow-up: [ADR 0048](0048-editor-network-and-service-recovery.md) adds bounded network and owned-service outage recovery. The broader outage, upload, browser/device and deployment matrix still remains.
