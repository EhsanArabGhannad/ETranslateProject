# ADR 0014: Signing preparation is immutable planning, not a signature

Status: Accepted for the development slice

## Context

The editor, archived unsigned PDFs, internal review, office teams and translator assignment exist. No real e-İmza/Mobil İmza provider has been selected or integrated. We need a tenant-scoped plan for eventual signing without representing internal approval, a proposed declaration, an image or a simulated callback as a valid signature.

## Decision

Trust owns `ETranslateTrust`, its initial EF migration, immutable preparation metadata, ordered planned stages and EF bus outbox. Other service databases are never read directly. Identity access and individual current signer eligibility are checked with the caller's forwarded bearer authorization. Workflow supplies the job's signature policy; it must match the current tenant type. Active Owner/Administrator can prepare/cancel; other active members can read history. A translator-stage target must be an active Owner/Administrator/Translator member of the same tenant. An office-stage target must be an active Owner/Administrator. These membership roles are **not evidence of legal translator qualifications or signing authority**.

Independent tenants get one translator stage. Office tenants require translator then office stages (orders 1 and 2). An eligible person may occupy both roles; we do not invent a distinct-person legal requirement. Assignment is a UI suggestion only, not evidence of who wrote, reviewed or will sign. Optional notary and acceptance profiles remain unchanged.

Documents exposes authenticated `GET .../documents/{documentId}/signing-artifact`. It joins the current approved internal review and its exact PDF, verifies revision/template/hash metadata and rechecks the actual archived bytes against bounded size and SHA-256, then rechecks the review round. Nonapproved documents return no artifact. Missing/corrupt PDFs or inconsistent metadata fail closed. Trust checks client expected review/PDF IDs against this response, validates signers, and fetches the artifact again before persisting. The stored snapshot includes review/draft/PDF/template IDs, round/revision/hash/size, source-file IDs/hashes/sizes, approval actor/time and actual preparation actor/time.

The reference artifact remains `DraftUnsigned` with its existing watermark. A preparation does not mutate it, create a final PDF, append statements, attach QR, change Workflow status or imply encryption/PDF/A/qualified signatures. Each stage stores the exact **proposed** statement (required, at most 4000 raw UTF-16 characters), intended signer ID and requested `EImza`/`MobilImza` method. This is not consent, an oath accepted by that person or a signed declaration. No legal declaration boilerplate is supplied automatically.

Only `Prepared` and `Cancelled` are supported. There is no Signed status, completion callback, fake signing bridge, token PIN/private-key collection or provider dispatch. Provider capabilities explicitly report NotConfigured/false. Authorized dispatch of a prepared request returns 501 and never changes data. A future integration must create and pin a distinct final signable artifact **before** its first cryptographic signature, capture real signers' approval of declarations and verify provider results; it must not append QR/statements to an already signed PDF arbitrarily.

SQL enforces one Prepared request per tenant/document with a unique filtered index. Cancellation requires expected version and a bounded nonempty reason; optimistic concurrency prevents two successful terminal transitions. Cancellation retains the original snapshot and stages and records actor/time/reason. Creation/cancellation and metadata-only integration events use one EF bus outbox save; events omit statements, reasons and email addresses. No event consumers or notifications are added.

Reopening a document does not silently rewrite or cancel old requests. GET derives `IsCurrentArtifact` from current approved review/PDF/hash; an unavailable artifact check is explicitly marked `ArtifactServiceAvailable=false` (the per-item false flag must not be interpreted as proof of staleness then). History still exists and the API allows authorized cancellation without Documents/Workflow being available. The Web page must resolve the document first, so complete Documents failure can prevent opening the page; use the scoped API or restore the service before UI cancellation.

The checks are cross-service snapshots, **not distributed locks**. Approval or membership may change after preparation checks. An already-authorized request may finish, consistent with prior ADRs. A retained request is not a reusable authorization grant: any future dispatch must revalidate current review/artifact, tenant access, each signer, authority, consent, final PDF and real provider configuration. This slice intentionally cannot dispatch.

Web remains a cookie-authenticated BFF with antiforgery, private/no-store responses, HTML-encoded statements and no automatic unsafe-method retries. The separate page is linked from the editor in another tab to preserve unsaved translation text. Managers see at most 200 eligible member choices; historical/noneligible IDs fall back to IDs. Latest 20 preparation records are displayed; older records remain stored. Invalid/conflicting create forms retain proposed text for copying. Lost/ambiguous responses require loading current state before trying again.

## Limits

No real signing, mobile operator integration, timestamp/long-term validation, public verification, final signed PDF, e-suret, office seal, legal acceptance, notary integration or payment/entitlement enforcement is claimed. Production needs provider choice, legal/recipient requirements, signing-authority and qualification checks, storage/access/retention hardening, abuse controls, richer paging/search and deployment security. Proposed declarations should contain no passwords, PINs or keys.

## Verification

Domain/model and dependency tests cover stage count/order, required raw-bounded proposals, methods, artifact guards, immutable cancellation, optimistic concurrency/filtered index, disabled capabilities and fail-closed dependency authorization. `Verify-SigningPreparations.ps1` runs Web/team baselines and live API/MVC checks for tenant isolation, role/signer eligibility, duplicate/racing requests, spoofed metadata, staleness/reissue, internal PDFs, disabled dispatch, antiforgery, encoded/preserved input and independent/office paths. Its fixtures are development-only and deliberately retained for inspection. Browser QA checks the visible preparation form/history separately; no actual agreement or signature is submitted.
