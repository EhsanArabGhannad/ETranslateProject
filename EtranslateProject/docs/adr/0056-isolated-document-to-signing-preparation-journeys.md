# ADR 0056: Exercise the document-to-preparation journey with real services

Date: 2026-11-04

Status: Implemented as opt-in acceptance

## Scope

Add two synthetic journeys against six real executables and six fresh SQL catalogs: a translation office with a distinct translator and manager, and an independent translator. Use the existing guarded QA host, pinned Chromium and owned browser contexts; never start AppHost or reuse application catalogs. No provider is connected and dispatch must remain HTTP 501.

This is hybrid API/browser acceptance. Registration, tenant/trial, invitations, job/document/template creation, source upload, review decisions and lifecycle probes use authenticated real HTTP APIs. Browser steps exercise login, rich editing/save/reload, approved-state lock, preparation forms, preview generation and the private BFF download. It is not a claim that every creation/approval screen was clicked end-to-end.

## Checks

The office case has NotRequired and an omitted acceptance profile; the independent case has Required/Physical and Other. These metadata choices must not change one-stage versus two-stage signature policy or create a notary approval. Pin a versioned letterhead, keep its initial draft, upload a source and save revision 2 through the actual editor. Check draft authorship, the source snapshot in review, immutable PDF ID/hash and rendering success.

Reject preparation before submission and while approval is pending. Reject translator approval/management in the office case. Approve as owner and require the mounted editor to remain read-only after reload. Submit proposed declarations through the manager form; preserve/escape script-shaped text. Verify the manager/translator stage identities and the independent form's absence of an office stage. Generate unsigned previews, verify API and private BFF download SHA-256 and parse PDFs. Each short draft has one page; its preview has that original page, an overview page and a separate page for each proposed stage: four office pages and three independent pages. Repeated generation must return the same archived preview.

Reject cross-tenant preview retrieval. Reopen approval and require the old plan to become noncurrent, reject generation against that stale plan, cancel with optimistic version checking and reject repeated stale cancellation. Keep draft JSON and original PDF unchanged. Inspect Documents and Trust SQL state and run the read-only storage inventory: six referenced physical keys, no orphan/missing/mismatched file. Browser JavaScript errors, CSP violations and external browser requests must be empty.

The test suite writes synthetic PDFs, screenshots and a structured report under ignored tmp. It removes its six SQL catalogs and records cleanup separately. Preliminary harness failures concerned account-ID lookup and an empty unsupported layout fixture; the fixtures were corrected to use the access endpoint and DocumentPageLayout.Default. No product code changes were required by this slice.

## Limits

This covers two short workflows on local Windows Chromium, active trial accounts and one owner/translator pair. It is not full release acceptance, a browser/role/subscription matrix, real signer consent, e-imza/Mobil Imza, notary processing, verification QR issuance or a signed final artifact. Reissue after a new approved revision, provider outages and deployment remain separate. Existing narrower tests remain relevant. See the [Persian guide](../testing/document-journey.fa.md).
