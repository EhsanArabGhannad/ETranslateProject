# ADR 0024: Versioned all-pages or last-page custom footer

Status: Accepted for the development slice

Date: 2026-10-15

## Decision

Step 4 adds optional `footerPlacement` to existing page-layout JSON, accepting only AllPages/LastPage. Omission means AllPages and default serialization retains the exact legacy field order/shape. Strict parsing still rejects duplicates, unknown fields, unsupported values and wrong types; unknown layouts remain readonly and verbatim in the form, even against forged inputs. No migration, service boundary, endpoint authorization or role/subscription policy changes.

Settings/Templates exposes the policy with the page-layout permission/compatibility checks. Existing content-revision optimistic concurrency, partial upload recovery and immutable template identities remain authoritative. Existing documents retain their pinned revision; later policy edits never change those documents or archived PDFs. The draft renderer cache version advances to v10-footer-policy because rendering behavior changes, without deleting old cache entries/files.

The production renderer measures and reserves the same footer space on every page for both modes. This intentionally preserves pagination and prevents body/footer overlap; reclaiming earlier-page whitespace is not part of this change. AllPages overlays the measured header/footer on every body page. LastPage overlays header-only on earlier pages and header/footer on the final page. With no header, earlier pages receive no custom overlay; with no footer the normal header behavior remains. Single-page documents receive the custom footer. Overlay inputs must each remain one page. Existing 35/45 mm footer/header, image, page and byte limits are unchanged. Standard unsigned notices and page numbering remain on every page, independently of the custom footer.

Live form and editor previews provide an explicitly hypothetical Last/Earlier page selector, not pagination or a print engine. It hides/shows the custom footer using the selected/pinned policy and warns about unknown values. Actual print verification uses the generated saved-draft PDF. Signing previews import the archived source pages unchanged before adding planning-only appendix pages: LastPage refers to the translation PDF, not the end of the combined appendix. SigningPreviewPolicy is not bumped because appendix generation and source import behavior do not change.

Step 5 late template changes, step 6 Turkish UI and step 7 final rollout remain pending. This does not enable signatures, payments, notarization, HTML print-to-PDF or real printer access. Existing dependency warnings in ADR 0021 remain separate production-readiness work.

## Verification

Unit tests cover default byte compatibility, both policies, typed form round-trip, revision preservation and wrong/unknown/duplicate values failing closed. Browser tests select, save and reload LastPage and toggle hypothetical previews. Opt-in local tests render all-page, last-page, single-page, no-header and empty-footer samples. A pypdf checker inspects every page for exact custom-footer presence, independent header presence, unsigned notices and page counts; all/last body pagination must match. HTTP tests check persisted policy, pinned documents, unchanged historical layout bytes, rejected writes before upload and archived PDF identity. Rasterized visual review is additionally required for layout. All fixtures are synthetic and retained only in local development.

Local verification on 2026-10-15: build without warnings/errors; 492 domain + 2 architecture tests passed (14 opt-in domain tests and one placeholder skipped in the ordinary run); JavaScript 42 passed. Opt-in template baseline: 88 editing/presentation + 224 navigation/team checks and two Chromium tests. Footer suite: 113 additional checks, including 82 semantic checks across 19 pages (5 all-page, 5 last-page, 1 single-page, 4 no-header, 4 empty-footer). Every sample page was rasterized with Poppler and visually inspected for placement, clipping and overlap. Previous regression suite: 664 HTTP checks + 11 SQL tests passed. The browser save assertion now waits for returned template identity/revision rather than a potentially already-fired navigation lifecycle event. No user process was stopped; agent-owned local hosts are shut down after verification. Ignored QA files and synthetic SQL fixtures remain for inspection.
