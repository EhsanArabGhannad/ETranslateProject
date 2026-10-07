# ETranslate

ETranslate is a multi-tenant B2B SaaS platform for preparing, signing, verifying, and optionally notarizing translated documents in Türkiye.

## Architecture

The repository is a distributed monorepo. Each service is independently deployable and owns its data. Cross-service communication uses versioned APIs and integration events.

## Initial services

- Identity & Access: users, tenants, memberships, roles, and translator credentials.
- Translation Workflow: translation jobs and their lifecycle.
- Documents: templates, source files, rendering, immutable PDF versions, hashes, and version-bound internal review.
- Trust: electronic signatures, timestamps, validation, and public verification.
- Billing: plans, 30-day trials, subscriptions, entitlements, and usage.
- Notary Integration: physical, digital, and hybrid notary workflows behind an anti-corruption layer.
- Notifications: asynchronous email, SMS, and in-app notifications.

## Local development

The solution targets .NET 10 and uses Aspire for local orchestration and observability.

### Prerequisites

- .NET SDK 10.0.400 or newer in the 10.0 feature band.
- SQL Server 2022 or newer. SQL Server Developer Edition is suitable for local development.
- A Windows-authenticated SQL Server login that can create the development databases on first run.

### Run the platform

```powershell
dotnet tool restore
dotnet build ETranslate.slnx
dotnet run --project src/Orchestration/ETranslate.AppHost/ETranslate.AppHost.csproj
```

Open the Aspire dashboard URL printed in the terminal. The services use one SQL Server instance while retaining database ownership boundaries:

- `ETranslateIdentity`
- `ETranslateBilling`
- `ETranslateWorkflow`
- `ETranslateDocuments`
- `ETranslateMessaging` for the MassTransit SQL transport

The checked-in development settings target `.\ESIMSSQLSERVER` with Windows Authentication. Change the AppHost connection strings through local configuration or user secrets when your SQL Server instance has a different name. No Docker or WSL installation is required for this setup.

### Tenant onboarding flow

1. Register: `POST /api/v1/auth/register` on Identity Access.
2. Log in: `POST /api/v1/auth/login` and keep the returned bearer token.
3. Create a tenant: `POST /api/v1/tenants` with one of these string values:
   - `IndependentTranslator`
   - `TranslationOffice`
4. Billing consumes `TenantCreatedV1` and creates a 30-day trial automatically.
5. The internal subscription can be inspected at `GET /internal/v1/subscriptions/{tenantId}` on Billing. This endpoint is for service-to-service access and is not a public authorization boundary.

Example tenant request:

```json
{
  "name": "Example Translation Office",
  "slug": "example-office",
  "type": "TranslationOffice"
}
```

### Database migrations and tests

Each stateful service applies its committed EF Core migrations at startup. The MassTransit SQL transport initializes its messaging schema automatically.

```powershell
dotnet test ETranslate.slnx
```

Domain and architecture tests run without external infrastructure. Local end-to-end verification uses the configured SQL Server instance and does not require a container runtime.

With AppHost running, verify template images, tenant isolation, pinned revisions and competing draft saves using PowerShell 7:

```powershell
./tests/ETranslate.EndToEndTests/Verify-TemplateAssets.ps1
```

This check creates isolated development tenants and leaves its test records and images available for inspection.

### Internal translation review

Save the translation, create its draft PDF, then use **بازبینی و تأیید داخلی** in the editor to submit that exact saved version. The PDF can be selected immediately after generation without losing editor text. Pending/approved documents lock draft saves and source uploads. Owner/Administrator can approve, return with a reason, or reopen an approval for corrections; submitters can withdraw their pending submission with a reason. Returned/reopened translations require a new saved revision and PDF before resubmission. Independent owners may self-review internally. Review history retains actors, UTC timestamps, notes, exact PDF hashes and source-file snapshots.

This is internal preparation, not e-imza, mobil imza, a seal or notarization. It does not change the job-level signing status. Team invitations/role management are available; per-job review assignment is still future work. See [ADR 0011](docs/adr/0011-document-review-boundary.md) and the [Persian test guide](docs/testing/translator-workspace.fa.md).

With AppHost running, PowerShell 7 can verify live review and competing requests:

```powershell
./tests/ETranslate.EndToEndTests/Verify-DocumentReviews.ps1
```

This test creates isolated development fixtures and leaves them for inspection. Do not run on production.

### Team membership and manual invitations

Owner/Administrator can open **مدیریت اعضا و نقش‌های این فضای کاری** from the workspace. Recipients must register an account first. A manager creates a seven-day, single-use invitation and personally delivers its confidential link to the intended person; no email is sent. Acceptance requires both the bound recipient account and the secret code. Only its SHA-256 hash is stored. The code is displayed once, never returned in team listings or integration events, and carried in a URL fragment that the browser removes before submission.

Owner can manage Administrator/Translator/Reviewer members. Administrator can manage only Translator/Reviewer members. Nobody can change their own membership or the Owner. Reviewer currently means **read-only observer**, not an approver; only Owner/Administrator approve internal reviews. Membership can be deactivated/reactivated without deleting the account or document history. Changes apply to subsequent authorization checks, not requests already in flight. Ownership transfer, seat billing, email delivery, verified-email onboarding and per-job assignments are not implemented.

Team mutations use a tenant concurrency version and append an audit record plus an outbox event in the same transaction. Stale requests return 409. Pending invitations, including expired ones, must be cancelled before reissue. An inviter must still have the required role when their invitation is accepted. See [ADR 0012](docs/adr/0012-tenant-team-invitations.md) and the [Persian team test guide](docs/testing/tenant-team.fa.md).

With AppHost running, verify roles, invitation races, antiforgery and cross-service revocation using PowerShell 7:

```powershell
./tests/ETranslate.EndToEndTests/Verify-TenantTeam.ps1
```

This creates isolated local development accounts and records, leaves them for inspection, and must not be run against production. Existing memberships are backfilled as active by the new Identity migration.

### Translation jobs

Tenant members can manage draft translation jobs through:

- `POST /api/v1/tenants/{tenantId}/translation-jobs`
- `GET /api/v1/tenants/{tenantId}/translation-jobs`
- `GET /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}`
- `PUT /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}`

The bearer token is checked against Identity Access for every tenant-scoped operation. `AcceptanceProfile` may be omitted or null. When `NotaryRequirement` is `Required`, `NotaryProcessingMode` must be `Physical`, `Digital`, or `Hybrid`.

Example without a notary or special acceptance profile:

```json
{
  "title": "Tourist visa passport translation",
  "sourceLanguageCode": "fa",
  "targetLanguageCode": "tr",
  "notaryRequirement": "NotRequired",
  "notaryProcessingMode": null,
  "acceptanceProfile": null,
  "acceptanceProfileOther": null
}
```

### Document templates

Tenant members can create reusable, tenant-isolated document templates. Template content is versioned immutably so an older signed or rendered document can continue to reference the exact layout that produced it.

- `POST /api/v1/tenants/{tenantId}/document-templates`
- `GET /api/v1/tenants/{tenantId}/document-templates`
- `GET /api/v1/tenants/{tenantId}/document-templates/{templateId}`
- `POST /api/v1/tenants/{tenantId}/document-templates/{templateId}/revisions`
- `GET /api/v1/tenants/{tenantId}/document-templates/{templateId}/revisions`
- `GET /api/v1/tenants/{tenantId}/document-templates/{templateId}/revisions/{revisionNumber}`
- `PUT /api/v1/tenants/{tenantId}/document-templates/{templateId}/status`

Each revision stores editor body content, optional header and footer content, page-layout settings, and an optional watermark definition as validated JSON. `ExpectedCurrentRevision` prevents concurrent editors from overwriting a newer template version. Archiving a template prevents new revisions without deleting its history.

Example template request:

```json
{
  "name": "Tourist Visa - A4",
  "description": "Office letterhead for visa translations",
  "editorContentJson": "{\"type\":\"doc\",\"content\":[]}",
  "headerContentJson": "{\"type\":\"header\",\"content\":[]}",
  "footerContentJson": null,
  "pageLayoutJson": "{\"pageSize\":\"A4\",\"orientation\":\"Portrait\",\"marginsMm\":{\"top\":25,\"right\":20,\"bottom\":20,\"left\":20}}",
  "watermarkJson": "{\"text\":\"TRANSLATION\",\"opacity\":0.12,\"rotation\":-35}"
}
```

### Translation documents

Template images can be uploaded as `multipart/form-data` field `file`:

- `POST /api/v1/tenants/{tenantId}/document-templates/{templateId}/assets`
- `GET /api/v1/tenants/{tenantId}/document-templates/{templateId}/assets`
- `GET /api/v1/tenants/{tenantId}/document-templates/{templateId}/assets/{assetId}`

PNG and JPEG up to 5 MiB are accepted, with a MIME/binary-signature check and SHA-256 metadata. Use `assetId` in editor JSON, for example `{"type":"image","attrs":{"assetId":"<uploaded-guid>"}}`. Create a template first, upload its assets, then save a new template revision with these IDs. References to another template's assets are rejected. Assets cannot be replaced or deleted through the API.

Apply a template before the document's first editor save:

- `POST /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/template`
- Body: `{"templateId":"<template-guid>","revisionNumber":2}`
- `GET` on the same path returns the pinned layout revision, including after the template is archived or updated.

Application creates draft revision 1 from the template body and pins its exact `TemplateRevisionId`. A repeated application or applying to an already edited document returns HTTP 409. Later draft saves start with `expectedCurrentRevision: 1` and may reference assets from the pinned template. The Web workspace uses these APIs; unsigned draft PDF export is available, but signed final PDF is not implemented yet.

Each translation job can have one translation document in the first product phase. Create it with:

- `POST /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents`
- `GET /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents`

Editor saves are immutable revisions. `ExpectedCurrentRevision` prevents a stale browser tab from overwriting newer work:

```json
{
  "expectedCurrentRevision": 0,
  "editorContentJson": "{\"type\":\"doc\",\"content\":[]}",
  "plainText": ""
}
```

Revision endpoints:

- `POST /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/draft-revisions`
- `GET /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/draft-revisions`
- `GET /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/draft-revisions/{revisionNumber}`

Source files are uploaded as `multipart/form-data` with field name `file`. PDF, JPEG, PNG, and TIFF files up to 25 MiB are accepted. The service checks the binary file signature and stores a SHA-256 digest for every upload.

- `POST /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/source-files`
- `GET /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}/documents/{documentId}/source-files/{sourceFileId}`

The default development blob provider stores content under the current user's local application-data directory. Production must replace it with shared object storage through `IDocumentBlobStore` before scaling the Documents service horizontally.

### Web translator workspace

Start the AppHost, then open [the local Web UI](https://localhost:7049). The interface currently uses Persian labels with automatic document-text direction; Turkish/localized UI is future work.

1. Register or log in with an Identity Access account.
2. Create/select a workspace (independent translator or translation office).
3. Create a translation job. Notary is optional; acceptance profile can be blank or Other. Notary mode only records intent, not an available notary integration.
4. Optionally create a template with header/footer/starter text/watermark and attach a PNG/JPEG logo. Expand page layout to choose A4/A5/Letter/Legal, portrait/landscape and margins (5–50 mm). Attaching a logo creates a new immutable template revision and appends to the existing header. An uploaded asset may remain unattached if the subsequent revision conflicts; inspect assets through the API before retrying.
5. Start the document, optionally pin a template before the first save, and save translation-body revisions. History is read-only; the pinned template does not change when the template is updated.
6. Upload original PDF, PNG, JPEG or TIFF files (up to 25 MiB each) beside the translation. PDF supports page navigation and zoom; PNG/JPEG are displayed as images. TIFF and password-protected PDFs are download-only. Uploading a source does not create a translation revision or clear unsaved editor input. Source files belong to the document and are shared across its translation revisions.
7. Format recognized documents with bold, italic, underline, headings, bullet/numbered lists and RTL/LTR/automatic direction. Undo/redo is available within the current browser session. Open the expandable letterhead preview separately.
8. Save the translation, then choose draft PDF generation. This exports the displayed saved revision only, not unsaved editor changes. Repeated exports reuse the same archived bytes. Previous PDFs remain downloadable after later draft/template edits. Every page says `DRAFT - UNSIGNED`; header/footer/logo and page counters repeat across pages. Historical revisions can also be exported.

This is an initial rich-text editor, **not a Word-equivalent document editor**. The original revision JSON remains the source of truth; plain text is only derived metadata. Both Web and browser validate a restricted schema before opening rich editing. Unknown JSON stays in an advanced field, without automatic conversion or flattening. The preview uses a limited, safe node vocabulary (paragraphs, text/marks, headings, lists, quotes, hard breaks, managed template images). Unsupported elements are flagged and PDF generation rejects them, rather than losing content. Browser preview page metrics/watermark remain approximate; download the draft PDF to inspect print pagination. There is no table UI, Word import, paginated WYSIWYG editor, signed final translation PDF, signature, payment checkout, or notary approval yet. PDF source previews are canvas images, without OCR, text selection, search, annotation links or PDF scripts.

### Draft PDF runtime and API

The print engine uses native Chromium through pinned Playwright, with embedded Noto fonts. Build and install the matching runtime once (PowerShell 7, from the solution directory):

```powershell
dotnet build ETranslate.slnx
./scripts/Install-PdfBrowser.ps1
```

Default installation is `D:\Github\ETranslateProject\.local\pdf-browsers` in this checkout. No Docker/WSL is required. AppHost supplies that path to Documents. With a custom install directory, configure AppHost `PdfBrowserPath`; direct/published Documents needs `PLAYWRIGHT_BROWSERS_PATH` pointing to its own matching runtime. Reinstall after a Playwright upgrade. Font files and license are included in Documents build/publish outputs. Missing browser returns HTTP 503 without changing drafts. Runtime installation needs Internet access; PDF requests do not fetch remote fonts or images.

- `POST .../documents/{documentId}/draft-revisions/{revisionNumber}/pdfs`: create or reuse a PDF (201/200).
- `GET .../documents/{documentId}/pdfs`: list immutable draft PDFs.
- `GET .../documents/{documentId}/pdfs/{pdfId}`: private PDF attachment with SHA-256/revision headers and byte ranges.

The prefix is `/api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}`. Metadata includes `kind: DraftUnsigned`, draft/template revision IDs, renderer version, hash and creation time, not storage paths. Unknown content/layout returns 422, occupied renderer 429, unavailable engine 503. No QR, e-imza, PDF/A, PDF encryption or legal acceptance is claimed. Browser export preserves unsaved editor input with JavaScript; without JavaScript its normal redirect cannot preserve unsaved input, so save/copy first. The draft watermark is a label, not protection against editing. See [the architecture decision](docs/adr/0010-immutable-draft-pdf.md) for limits and deployment hardening.

Source downloads pass through authenticated, tenant-authorized Web routes with private/no-store responses and byte-range support. The Documents service remains authoritative for binary-signature validation. Web buffers at most 25 MiB per download; range requests currently fetch the entire upstream source before returning the selected range. Large-scale production use needs streaming/range forwarding, resource limits and malware scanning; MIME checks alone are not antivirus scanning.

The browser assets are built locally from pinned [Tiptap](https://tiptap.dev/docs/editor/getting-started/install/vanilla-javascript) and [PDF.js](https://mozilla.github.io/pdf.js/) dependencies, with their license notices included. No CDN is used. The CSP allows WebAssembly compilation for PDF decoding, but not general JavaScript `unsafe-eval` or inline scripts. PDF.js evaluation/XFA and font-face injection are disabled. Committed generated assets let normal .NET builds/runs work without Node.js.

To change browser code, use Node.js 22.13 or newer and rebuild from `src/Web/ETranslate.Web`:

```powershell
npm ci
npm test
npm run build
```

Rebuild/restart Web and reload the browser after asset changes. Client rebuilding is a separate step, not an automatic .NET build target. See the [Persian manual test guide](docs/testing/translator-workspace.fa.md).

Web acts as a Backend-for-Frontend, calling Identity/Workflow/Documents via service discovery without API-project references or database access. Gateway remains a health-only placeholder, not a reverse proxy. Identity tokens live in an encrypted HttpOnly authentication ticket, never browser storage or client-side JavaScript. The non-sliding session expires with the access token; refresh tokens are not stored. All MVC mutations require antiforgery validation. Browser saves preserve the current text on session expiry, network failures or conflicts; conflicts require manual comparison with the latest version. Without JavaScript, ordinary form posts work but template selection/preview and expiry/network input preservation are limited. There is no autosave or local draft backup; keep a copy before closing a failed save.

Production needs HTTPS, shared/persisted protected Data Protection keys and deployment hardening; this first UI slice is not a production-release claim. Cookie logout clears the Web session, not all already-issued Identity bearer tokens. Unsafe HTTP methods are not automatically retried by Web's resilience handler, to avoid duplicate mutations after ambiguous timeouts.

With AppHost running, repeatable live checks (PowerShell 7):

```powershell
./tests/ETranslate.EndToEndTests/Verify-WebWorkspace.ps1
./tests/ETranslate.EndToEndTests/Verify-TemplateAssets.ps1
./tests/ETranslate.EndToEndTests/Verify-TranslatorWorkspace.ps1
./tests/ETranslate.EndToEndTests/Verify-DraftPdfs.ps1
```

The translator check also runs the Web workspace baseline; it tests private sources, MIME/size/antiforgery restrictions, byte ranges, tenant isolation, exact rich JSON preservation, pinned templates, stale saves and unknown-schema fallback. The scripts create isolated development accounts/tenants/documents and leave their fixtures for inspection. Never run against production. `dotnet test` alone does not execute these HTTP smoke scripts; the existing EndToEnd xUnit placeholder remains skipped.
