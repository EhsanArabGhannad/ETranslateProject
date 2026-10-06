# 0010 - Immutable unsigned draft PDFs

Status: Accepted for the development slice.

## Decision

Documents owns rendering behind `IDraftPdfRenderer`, using pinned Microsoft.Playwright 1.63.0 and native Chromium headless shell. PDFsharp 6.2.4 applies a separately rendered vector letterhead/footer page onto every body page: this avoids unreliable Chromium fixed-element/table-header repetition, preserves embedded fonts, and reserves print margins before body pagination. It reads only renderer-generated PDFs, not uploaded sources. Web remains a BFF; no Web database or API-project dependency is introduced. SQL Server stores PDF metadata and `IDocumentBlobStore` stores private bytes. No Docker, WSL or personal browser profile is needed.

A PDF is generated from an exact immutable draft and the document's pinned template revision. The unique `(DraftRevisionId, RendererVersion)` index makes repeats return archived bytes, not a newly generated timestamp-dependent PDF. Concurrent losers remove only their newly written blob. A new draft or renderer version produces another immutable artifact. Bump `DraftPdfPolicy.RendererVersion` when print layout, fonts, schema interpretation or browser version changes. Existing PDFs remain available; template edits/archival cannot silently alter them.

The supported editor schema is shared by Contracts and Web. Rendering validates the entire JSON tree before converting anything. Unknown nodes, attributes, marks, layout or watermark settings fail with 422; original JSON is never flattened or rewritten. Legacy `doc` header/footer roots and dedicated `header`/`footer` roots are explicitly supported.

A4/A5/Letter/Legal, portrait/landscape, bounded margins, rich text, RTL/LTR, repeated letterhead/footer, managed logos, watermark and page counters are supported. Fonts are embedded Noto Sans and Noto Sans Arabic, with pinned provenance and OFL license in `Rendering/Fonts`. HTML preview is still approximate; downloaded PDF is the print artifact.

Every page is marked `DRAFT - UNSIGNED`. This is not e-imza/mobil imza, a seal, QR verification, PDF/A archival compliance, encrypted PDF, notary approval or a guarantee of acceptance. SHA-256 identifies stored bytes; it is not an identity signature or public authenticity proof.

## Resource and security boundaries

- Every create/list/download checks document, job, tenant and authorized membership. MVC creates require antiforgery. Downloads are private/no-store attachments; Web verifies size/hash and supports ranges.
- Text is HTML encoded. No user HTML/CSS/scripts, hyperlinks, filesystem URLs or remote assets are accepted. Renderer CSP restricts sources to embedded data fonts/images; JS and service workers are disabled and network requests are aborted. Chromium sandbox stays enabled.
- Rendering is limited to one operation per process; slot wait is one second (429), rendering budget 20 seconds, browser launch timeout 10 seconds. The PDF-specific BFF client allows a 30-second attempt / 35-second total without replaying unsafe methods. This is not a distributed queue, per-tenant quota or load-tested production worker.
- Limits: 100,000 combined plain-text characters, 10 unique images, 10 MiB combined image bytes, 5 MiB per image, 16 million decoded pixels, header 45 mm/footer 35 mm, minimum body height 60 mm, 50 PDF pages, PDF 25 MiB. Layout estimates reject oversized bodies early and PDFsharp enforces the exact final page count. PNG/JPEG signature checks are not malware scanning. Renderers still need OS resource restrictions in production.

## Deployment

Build Debug and run `scripts/Install-PdfBrowser.ps1` once. The default runtime directory is the repository-root `.local/pdf-browsers` (ignored by Git), on D: in this checkout. AppHost sets `PLAYWRIGHT_BROWSERS_PATH`; override `PdfBrowserPath` for another absolute directory. When launching Documents directly or publishing, install with the Playwright script shipped with that matching build and set the environment variable yourself. Font files must ship with the service. Nothing downloads automatically on first request; unavailable runtime returns 503.

Production requires compatible Chromium/OS dependencies, sandbox support, restricted service account, CPU/memory/process quotas, explicit runtime upgrade policy, storage backup/retention, shared object storage and cross-instance admission control before horizontal scale. The native process may be moved behind the same interface to an isolated print service later. No notary integration or signature workflow is bundled into this slice.

## Verification

Unit tests cover layout/schema/encoding/watermark/resource checks. `Verify-DraftPdfs.ps1` exercises live SQL-backed versioning, templates, antiforgery, private downloads, byte ranges, hashes, cache identity, cross-tenant denial and unknown JSON preservation. It exports a synthetic multi-page bilingual sample for page-by-page visual review. Domain tests alone do not exercise Chromium or SQL.

References: [Playwright PDF](https://playwright.dev/dotnet/docs/api/class-page#page-pdf), [browser installation](https://playwright.dev/dotnet/docs/browsers), [HTTP resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience), [PDFsharp](https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html).
