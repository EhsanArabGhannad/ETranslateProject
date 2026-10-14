# ADR 0020: Bounded simple translation tables

Status: Accepted for the development slice

Date: 2026-10-13

## Decision

Extend the existing JSON vocabulary, browser editor and unsigned PDF renderer with simple rectangular tables. This advances document preparation without a signing/payment/notary provider. No database migration or service ownership change is needed; immutable document JSON remains the source of truth, and old revisions/PDFs are never rewritten.

Supported nodes are `table`, `tableRow`, `tableCell`, `tableHeader`. Tables are direct doc children only, never nested inside cells/lists/quotes. Each table has 1..50 rows, each with the same 1..12 cells. Each cell contains one or more paragraphs/headings/lists/quotes, using existing marks and direction. Optional cell defaults `colspan=1`, `rowspan=1`, `colwidth=null`, `align=null` round-trip from pinned Tiptap 3.31.4. Merged cells, fixed widths, non-null alignment, unknown styling/attributes, empty/ragged/nested tables are unsupported, not normalized. Existing node/depth/text/JSON limits still apply. C# and JavaScript validators implement the same policy; tests exercise both plus the actual pinned ProseMirror defaults.

The browser uses the matching pinned `@tiptap/extension-table`, with resizing disabled and the table node view disabled in favor of external CSS. Toolbar insertion creates 3x3 with a heading row. Row/column insertion and deletion, table deletion, cell text formatting, undo/redo, saving and historical readonly behavior are supported. The selection must be at a top-level paragraph/heading to insert; inside a cell to modify. Toolbar guards per-table size, and a transaction filter independently rejects changes that leave the preservation vocabulary or exceed limits (including keyboard-created rows). Unsupported JSON retains advanced fallback; the body preview refuses it rather than drawing an apparently unmerged table. Full Word/Excel paste fidelity is not claimed.

PDF output uses semantic table/tr/th/td, fixed equal-width columns, encoded content and existing embedded fonts. An initial all-header row becomes `thead` for repetition across printed pages; later header cells remain semantic th without additional repeat groups. Normal rows avoid splitting across pages. After fonts/images load, the renderer checks the tallest body row together with its repeating heading against printable page height; oversized rows yield 422 without creating an artifact rather than risking clipping. Existing print size/page/text/image limits and no-network/no-script policy remain. Browser letterhead preview is not a paginated print guarantee; the downloaded PDF is authoritative for layout inspection.

`DraftPdfPolicy.RendererVersion` advances to v8-tables, so rendering old supported revisions with the new engine creates a new immutable artifact, not an overwrite of v7. Existing approved review/signing preparations retain their exact PDF/hash; changing engine capability does not replace approved sources or create signatures. PDFs remain `DraftUnsigned`, without QR or legal acceptance claims.

## Verification and limits

Unit tests cover limits, default attrs, row-major derived text, encoding, semantic repeat headings and rejection without source mutation. Browser-schema tests cover the pinned extension defaults. The explicit opt-in local Chromium test uses only the smoke script's synthetic account/job: insert/edit/delete/undo/redo/save/reload/preview and historical readonly controls. The local HTTP suite tests exact rich JSON, optimistic conflicts, immutable cached PDF/download/hash, unsupported fallback, oversized-row rejection, unauthenticated and cross-tenant access. Fixtures are retained, never run on production. Multi-page Persian/Turkish output must be rendered and visually checked before handing off this slice.

The existing rich-editing limitations remain: no merged/nested/resized tables, formulas, cell images, arbitrary alignment/styles, Word import, page-by-page WYSIWYG, autosave or signatures. A failed transaction preserves the current editor state; unsupported stored JSON is not automatically converted. Advanced JSON saves can store unsupported content as before, but cannot export it through this renderer. Server access, review locks, antiforgery and subscription gating remain authoritative and unchanged.

Implementation follows the [official Tiptap table commands](https://tiptap.dev/docs/editor/extensions/nodes/table), with project-specific preservation limits above; generic extension capabilities are not all exposed.
