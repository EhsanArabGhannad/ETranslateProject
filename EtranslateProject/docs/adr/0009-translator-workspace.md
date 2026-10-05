# ADR 0009: Rich editor and private source preview

Date: 2026-10-04

Status: Accepted for development

## Context

ADR 0008 established Web's BFF, immutable drafts and pinned template layouts. Translators now need to read an original document while preparing formatted translation text. This slice extends that workspace without changing service ownership or claiming signed/legal output.

## Decision

- Keep existing Documents APIs and storage. Web derives document IDs from tenant-authorized metadata before uploading or downloading sources. No public verification/download endpoint or new database coupling is introduced.
- Accept PDF/PNG/JPEG/TIFF sources up to 25 MiB. Upstream binary-signature checks remain authoritative. Buffer private responses with a hard bound and exact metadata-length check, then support browser byte ranges. File responses use no-store/nosniff and a restrictive sandbox CSP.
- Use locally bundled, version-pinned Tiptap for a restricted rich-text schema, and PDF.js for canvas-only source previews. No CDN, arbitrary image URLs, HTML injection, annotation layers, XFA or PDF script execution. Local PDF decoding resources require the CSP's specific wasm-unsafe-eval permission, not JavaScript unsafe-eval.
- Validate the editable JSON vocabulary on both server and client, including nesting/node budgets, allowed attributes/marks and managed image GUIDs. Server checks duplicate JSON properties before client parsing can discard them. ProseMirror structural validation is an additional eligibility gate. Normalize legacy empty, unmarked text nodes only; unknown content remains in the original JSON fallback field.
- Persist structured JSON, not preview HTML or flattened text. Derive plain text as metadata. Apply text direction at paragraph/block level. Only the translation body changes; pinned header/footer/assets/layout remain intact.
- Enhanced upload updates the source selector without navigating away from unsaved translation. Enhanced save freezes inputs while in flight; errors retain the page and formatting. A stale revision locks further saves until manual comparison. No autosave, persistent browser draft storage or automatic write retries.
- Commit generated browser assets and dependency lockfile. Node.js is only needed for browser changes, not ordinary .NET runs. Keep rebuild/test instructions and third-party license notices in the repository.

## Boundaries

- TIFF and encrypted PDFs are download-only. PDF preview has no text layer, OCR, search or password-entry flow. Browser editing has no Word import, tables UI or faithful page layout. A preview is not a final PDF.
- Each source download/range currently buffers the full upstream file, up to 25 MiB; concurrent downloads need production resource controls and upstream range forwarding. Binary signatures are not malware scanning.
- Sources are document-level, not snapshotted per translation revision. An old translation revision can therefore show sources uploaded later. No deletion/replacement workflow is introduced.
- Signing, QR verification, legal acceptance, notarization, payments, shared blob storage and production deployment hardening remain separate work.

## Verification

- Shared schema fixtures run in Node and .NET; server tests additionally reject duplicate properties and invalid file metadata.
- Live HTTP checks cover private uploads/downloads/ranges, size/MIME/CSRF restrictions, tenant isolation, unchanged draft counts after upload, exact rich saves and pinned templates, conflicts, history and unknown-schema preservation.
- Browser QA checks actual toolbar selection, persisted formatting/direction, upload without clearing dirty text, PDF pages/zoom and a single-column responsive layout. A synthetic two-page PDF is visually checked separately; it is a fixture, not product PDF generation.
