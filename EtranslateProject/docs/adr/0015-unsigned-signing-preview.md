# ADR 0015: Unsigned signing preview is a separate immutable planning artifact

Status: Accepted for the development slice

## Decision

No real signature provider is configured. A preview may illustrate proposed declarations, but must never create consent, a signed status, an office seal, a QR verification claim or a final signable PDF.

Documents owns immutable `DocumentSigningPreview` records in `ETranslateDocuments` and the corresponding private blobs. It imports the exact approved unsigned PDF's existing pages with PDFsharp, then appends an A4 planning-information page and one proposed-stage section per signer. It does not rerender or overwrite the translation, append to a signed PDF or create an entry in the draft PDF table. Original pages retain their original headers, footers, watermark and page numbering; appendix numbering is explicitly separate. A combined document is limited to 50 pages and 25 MiB. The appendix uses the existing sandboxed native Chromium renderer and embedded fonts, with encoded plain-text statements and no remote resources.

Trust exposes authenticated, tenant/job/document/preparation-scoped `preview-snapshot` JSON with the V1 shared contract. This endpoint reads only its database after Identity authorization; it does not call Documents. Documents forwards the caller's server-side bearer to it. AppHost adds a runtime Documents-to-Trust reference without a reverse startup `WaitFor`: Trust still waits for Documents, so no startup cycle is introduced. Neither service reads the other service's database.

Only active Owner/Administrator can generate, while active tenant members can list/download historical previews. Creation checks a Prepared/version-zero plan and the current approved document/review round, revision, template, PDF IDs, size and hashes. Actual bounded source bytes must match the archived SHA-256. After rendering and storing the new blob, Trust status and Documents approval are rechecked before SQL persistence. Reuse also rechecks current status and verifies both files. Stale or cancelled plans cannot create/reuse via POST; existing private files remain downloadable as historical snapshots. Current signer eligibility is not asserted by a preview: its signer IDs are account IDs, not certificate identities or evidence of legal qualifications.

The appendix labels all declarations proposed and NOT ACCEPTED, requested methods not enabled, and the entire file UNSIGNED / NOT a final signable PDF. It includes preparation/preview/review/source PDF IDs, source hash, revision/round and preparation actor/time. Its static text warns that later cancellation/reopening is not reflected in the PDF. Preview metadata and download headers expose a separate preview SHA-256 and clearly named source SHA-256, never a storage key or the private JSON snapshot. There is no public link or QR. No encryption, PDF/A, qualified signature or recipient acceptance is claimed.

SQL's unique `(PreparationId, RendererVersion)` index ensures repeated generation reuses the exact stored bytes, including the winner of concurrent creation. Renderer saturation returns 429 rather than queueing indefinitely. A failed persistence/recheck deletes only the newly created blob; an existing winning artifact is not deleted. File storage and SQL are not an atomic transaction: crash-orphan reconciliation remains future production work. This operation does not change Trust or Workflow status and emits no signing event.

Web remains an authenticated BFF, with antiforgery on creation, server-only tokens, encoded text, no automatic POST retries, private/no-store downloads, size bounds and SHA-256 verification. It loads at most 100 recent preview metadata records, and its preparation page shows the latest 20 plans. Older records remain stored and downloadable through the scoped API; comprehensive history pagination is future work.

## Limits and future integration

These are cross-service checks, not distributed locks. An already-authorized operation may finish if cancellation, approval or membership changes immediately after the final checks. Such a stored preview conveys no authorization to sign. Future dispatch must independently revalidate access, source approval, each signer's identity/eligibility/authority and consent, pin a distinct final signable artifact, then verify real provider results. It must not treat a preview hash, internal review or imported unsigned pages as a cryptographic signature.

Before production: replace local development blobs with shared private object storage; harden access, retention, resource quotas, crash reconciliation, audit/abuse controls and deployment; establish real legal/recipient/provider requirements. The feature is independent of optional notary/acceptance-profile settings and does not enforce subscriptions or payments.

## Verification

Domain tests cover contract scope, stage policy/order, proposed text limits, HTML encoding, unsupported state/method/artifact, exact original-byte preservation, page import order/dimensions, combined page limits, corrupt PDF input, immutable model/indexes and Trust lookup failures. `Verify-SigningPreviews.ps1` runs the existing Web/team/signing baseline, then authorization/isolation, private files and ranges, exact-byte reuse, original-PDF preservation, cancelled/stale plans, independent/office previews, concurrent rendering, full-length declarations and BFF/antiforgery checks. All fixtures are synthetic and retained only for development inspection. Browser QA creates and downloads a Persian/Turkish preview; Poppler page rendering verifies the sample, independent/office paths and multiline pagination. Raster comparison confirms imported source pages are visually identical in the exercised fixture.
