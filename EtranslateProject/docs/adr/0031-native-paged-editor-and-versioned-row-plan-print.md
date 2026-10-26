# ADR 0031: Opt-in native page flow and versioned table-row printing

Date: 2026-10-26

## Decision

Integrate the native-column experiment into the existing editor as an optional view. Continuous paper view remains the default. `Sayfalı düzeni aç` enables physical sheet boundaries, page count, previous/next and direct page selection. Columns flow horizontally inside the editor viewport. The document stays one editable DOM: no remount, saved page nodes, spacer paragraphs, copied table rows or provider commands. View changes preserve selection, history and JSON; the view preference is not persisted across reloads.

The controller uses the existing session's document/state notifications. Save/review freezes, historical read-only and role/subscription locks continue through their existing paths. Page navigation never calls `setEditable`. Focus mode, fit-to-width and explicit zoom remain view-only. Unsupported template structure, unavailable images, oversized rows or unsupported fragmentation cause a visible Turkish fallback to continuous view without changing the content or unlocking it. Missing assets are not silently omitted from the page model.

Local Noto fonts, physical margins and typography match draft printing. Template header/footer images and text are measured after font/image readiness; headers repeat, and footer placement follows the pinned template's AllPages/LastPage policy. Template decorations and page labels are outside the editable DOM. Body-image measurements use inert boxes with the decoded images' unscaled dimensions rather than initiating new private-image loads for each measurement. Existing authorization still owns actual asset requests.

## Shared plan and print version

`src/Shared/DocumentLayout/table-row-plan.js` owns the temporary native-column measurement and explicit table-row break plan. The research harness re-exports this source. The Web client build also generates a checked-in, embedded IIFE for Documents. Its source SHA-256 is normalized for line endings and verified by a Node test. Publishing Documents needs the packaged resource, not a Node runtime or downloaded script. Publishing Web includes the copied Noto assets and their OFL notice.

The existing Chromium renderer independently computes the row plan from its own validated HTML, font resources and geometry. Client coordinates, HTML or page plans are never accepted. Body rows must fit a single native fragment; merged/nested tables and more than 50 pages are rejected. Explicit breaks apply only to the temporary print DOM. The rendered page count must equal the independently measured count before the PDF is accepted. The plan's short text/cell fingerprint detects stale layout during synchronous application; it is not cryptographic evidence or a security boundary.

New draft renderer identity: `draft-html-v11-row-plan-pw1.63.0-noto-pdfsharp6.2.4`. The existing cache key includes revision and renderer version, and downloads remain by PDF identity. New output therefore gets a new stored file/identity/hash; an old v10 file is not overwritten, regenerated or relabelled. Existing review/signature references remain attached to their original PDF IDs and hashes. This was checked by source inspection and metadata tests; database-backed archive/download acceptance still needs the separate manual check.

Signing-preview appendices bypass the new plan and retain `signing-preview-v2-letterhead`; this ADR does not change that output contract. Renderer concurrency, deadline, resource budgets, disabled document JavaScript, CSP, request blocking, image validation, PDF overlay and integrity/storage paths remain in the existing renderer. Only the application's embedded planning function executes through the trusted renderer API.

No content vocabulary, descriptor, database migration, provider dependency or paid license changes. The older content-version migration has not been applied by this work. Product services/AppHost/SQL were not started.

## Verification and remaining scope

See the [Persian test guide](../testing/editor-pagination.fa.md). The production bundle is exercised under CSP on a synthetic loopback host with save/retry/reload and read-only states. An independently extracted PDF map checks every one of 47 actual product DOM markers, not just six equal page counts. The expanded native harness checks 15 supported synthetic cases and 641 markers against v11, including no-heading/mixed-heading tables, multiple tables, long paragraphs, landscape, repeated headings and last-page footers. Tall-cell rejection remains explicit. Selected PDFs are rasterized and inspected alongside product screenshots.

These results establish the tested inputs on the pinned Chromium build. They do not establish all line boundaries, pixel equality, every browser/OS, real SQL-backed authorization, Word import, manual page breaks or a complete word processor. The editor engine remains replaceable through the existing adapter; this view adds no Tiptap-specific dependency. Full role/tenant/subscription acceptance and live deployment remain separate work.
