# ADR 0043: Versioned manual page starts on body text blocks

Date: 2026-10-28

Status: Accepted for this development slice

## Contract and editor behavior

Rich schema 10 adds optional nullable boolean `pageBreakBefore` to paragraphs/headings. True is permitted only on top-level body text blocks; null/false may appear on nested text blocks as provider defaults. At most 49 top-level starts are accepted. Schemas 1..9 reject the field even when null. Header/footer content, including their legacy doc-root fallback, cannot contain a forced start. No page-number nodes, physical coordinates, new database migration, package or font are introduced.

The provider and explicit rich saves now write v10. Mount, selection and view changes leave historical source bytes/metadata untouched. Raw saves inherit authoritative metadata, plain fallback preserves known versions through v10, unknown v11 stays unsupported and downgrade protection remains. The template form keeps its v1 contract. Draft/PDF/review identities remain immutable; the signing appendix stays v2.

The application commands are `insertPageBreak` and `removePageBreak`; the host receives eligibility only. Insertion requires an empty caret in a top-level paragraph/heading. At the start of a later block it sets the flag without adding content. In the middle it splits the inline fragment, retaining text marks, direction, typography and paragraph attributes on both sides, and flags the right block. At the end of a heading the right block is an editable paragraph. The caret moves to the continuation. Insertion at the very start of the document or an already flagged block start is a no-op; an empty cover is not implicitly generated.

Removal clears only the forced start and retains paragraph separation. Undo restores the exact pre-split distribution. Each insert/remove is preflighted against the current vocabulary, node, text and JSON limits and forms an independent history step. Readonly, historical, save/review and conflict locks apply to both controls and shortcuts. Existing page-count/PDF limits remain separate: 49 manual starts do not guarantee that all intervening automatic content fits within 50 pages.

`Mod-Enter` uses the same insertion path; Backspace at the beginning of a flagged body block removes the forced start without deleting text. Ordinary Enter within that block clears only the right block's copied flag in the same transaction. This is handled explicitly because the installed vendor's middle-of-block split clones attributes despite `keepOnSplit:false`; the end-of-block behavior differs. A flagged paragraph cannot be wrapped into a list until the start is removed. This avoids silently losing a boundary or writing an unsupported nested flag.

Image insertion within a flagged paragraph retains the left text block and its start, including an empty left paragraph at offset zero, and clears the flag on the remaining right text. Consequently it preserves one start rather than duplicating or dropping it. The image remains a separate body block. All selection and split operations stay inside the provider; the application source remains independent of the editor vendor.

## Layout and rendering

The Turkish **Elle sayfa sonu** panel provides insertion/removal and explains scope, shortcuts, paragraph preservation and limits. A view-only arrow/title identifies flagged paragraphs in the continuous editor without consuming layout height. The paginated editor uses a column break; the PDF uses a page break. A declared start on the first body block is already at the beginning of the document and does not create an extra blank page, including after preceding content is removed.

The shared row planner temporarily measures these starts as column breaks, including its generated `main > div` wrapper on the server, and restores original styles before print. It includes the start positions in its local stale-layout fingerprint while retaining the old fingerprint when starts are absent. Starts occur outside tables, so table row contiguity, horizontal spans and repeated headers retain the prior policy. Both client and server measure their own geometry; no client layout is trusted.

The server runs the shared planner when a document has tables or manual starts. It compares the measured page count with the actual PDF and retains the 50-page cap. Nonzero-height empty paragraphs make an intentional empty final/intermediate page explicit. Template headers and the last-page-only footer use the resulting page count as before.

The renderer advances to `draft-html-v20-manual-page-breaks-pw1.63.0-noto-pdfsharp6.2.4`. Existing files are downloaded by identity and never rewritten; old content remains readable under its declared vocabulary.

## Verification

Shared Node/C# cases cover exact boolean/null types, old-version rejection, nested and header/footer rejection, the 49-start limit and a declared first start. Model/session cases cover marked RTL text, block attributes, middle/end/start behavior, headings, selection scope, image insertion, history and old-provider/readonly gating. The BFF receives a real v10 start payload and preserves it through rich-save validation.

Production-bundle browser cases cover controls, Ctrl+Enter, ordinary Enter, Backspace, image insertion, retry/save/reload and forced readonly events. Five PDF cases compare marker pages and forced-start top positions: plain text, a repeated-header table with a private image, an empty final page, a declared first start with an empty intermediate page, and the full 50-page limit. Last-page-only footers and repeated headers are verified separately. Representative pages and the controls are visually inspected.

Isolated SQL acceptance adds a forced start before `END_MARKER` through the actual UI, Undo/Redo, v10 persistence/reload and PDF marker comparison. That marker must start after all table rows. Existing image ownership, immutable archive and review-lock assertions continue to run against six fresh QA databases with guarded cleanup. See the [Persian guide](../testing/editor-page-breaks.fa.md) for executed results.
