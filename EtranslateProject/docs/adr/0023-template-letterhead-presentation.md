# ADR 0023: Versioned letterhead alignment, banners and live preview

Status: Accepted for the development slice

Date: 2026-10-15

## Decision

Step 3 extends step 2 without a schema migration. New presentation data lives in immutable header/footer JSON: paragraph `attrs.textAlign` accepts left/center/right; managed header image `attrs.align` accepts the same values and `attrs.layout` accepts logo/banner. Omitted values retain the legacy appearance. New attributes are valid only when DocumentContentJson is asked to validate a header/footer root. The doc/body editing vocabulary is deliberately not relaxed, so the rich editor cannot silently strip these template-only attributes. Legacy doc-root template regions remain supported without the new attrs; changing presentation upgrades that region to its explicit header/footer root.

The simple form can project only lossless plain paragraphs followed by managed images (header) or plain paragraphs (footer). Unknown attrs, rich text, interleaved images and unsupported layouts remain server-owned/readonly. Forged inputs cannot replace those parts. Each line has an independent alignment selection; image settings apply to all editable top-level header images when explicitly selected. Uniform saved image settings prefill the form; mixed/absent settings retain their original nodes. Unchanged JSON is preserved verbatim where possible. Existing expected-revision, tenant/role, subscription and antiforgery checks still precede mutations; no new API, trust boundary or remote asset fetch is introduced.

The browser preview builds DOM with text nodes, allowlisted styling and same-origin authorized assets or temporary local object URLs. It does not execute template HTML. The form preview shares the document preview renderer, updates before saving and scales the paper/spacing to the selected known dimensions. It is explicitly approximate, unpaginated and not a claim of PDF equivalence. Unknown content has a warning and original JSON is preserved. JavaScript is required for live preview and per-line controls; original create/edit POST compatibility is retained.

PDF rendering uses the same allowlisted attrs, with a logo capped at 40 by 20 mm and a banner constrained to printable width with a 30 mm height cap and object-fit contain. Banner means within margins, not bleed/edge-to-edge, not crop and not a background behind text. An image-only header is simply a blank text region plus a managed image. Total header/footer height limits, integrity checks, no-network renderer, page bounds and unsigned status are unchanged. Overheight regions fail instead of being clipped.

Because rendered bytes may change, DraftPdfPolicy.RendererVersion advances to v9-letterhead and SigningPreviewPolicy advances to v2-letterhead. Old archived PDFs are never overwritten. Regeneration can create a new PDF under the new renderer cache key; signing previews remain proposals only, never signatures or legal notarizations.

Footer frequency, late document template attachment, Turkish UI and final rollout stay steps 4–7. Existing dependency warnings from ADR 0021 remain separate production work; no packages were upgraded here.

## Verification

Unit tests cover context-specific allowlists, independent alignment round-trips, image reference preservation, exact legacy JSON, unchanged body policy and rejection before uploads. JavaScript tests cover safe text, image modes, replacement and preservation. Local HTTP tests cover actual scoped template revisions, selected form values, image-only headers and archived PDF cache identity. Disposable Chromium checks image bounds and line alignment before/after save, desktop/mobile overflow and generates a five-page sample through the production renderer for rasterized visual inspection. Existing subscription/signing/order suites provide regression checks. All fixtures are synthetic and retained locally, with no production or external writes.

Local verification: build succeeded with zero warnings/errors; 480 domain and 2 architecture tests passed, 14 domain opt-in tests and one end-to-end placeholder skipped in the ordinary run. JavaScript: 39 passed. The opt-in template suite passed 88 editing/presentation checks plus 224 baseline/navigation checks and two Chromium tests; the prior purchase-order suite passed 664 HTTP checks plus 11 SQL tests. The five-page PDF was rasterized with Poppler and visually checked across all pages, along with the desktop/mobile UI. Synthetic SQL fixtures and ignored QA files remain locally; no user-owned running process was stopped.
