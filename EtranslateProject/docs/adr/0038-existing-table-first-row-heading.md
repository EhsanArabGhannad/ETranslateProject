# ADR 0038: Change the first-row heading of an existing table

Date: 2026-10-28

Status: Accepted for this development slice

## Decision

Expose the engine-neutral action `toggleTableHeaderRow` on the main editor toolbar as **İlk satır başlığı**, with `aria-pressed` indicating whether every cell of the first row is a heading. From any cell/text selection wholly inside one top-level table, the action changes that table's first row. It does not change the current row unless that is the first row. A mixed first row becomes entirely headings; a fully headed first row becomes ordinary cells. No new row or duplicate heading content is inserted. Later header cells remain as they were.

The provider uses the existing `tableHeader` and `tableCell` nodes. It preserves each first-row cell's attributes, descendants and marks, and retains the selection. Other rows, dimensions, other tables, paragraphs and source text do not change. The first-row transaction is one history step with boundaries before and after, independent of neighboring typing. Selection/navigation and initial mount do not rewrite the stored JSON or mark it dirty.

The action is disabled outside a table and for selections that extend beyond one table, including whole-document and table-node selections. A cell selection inside the same table is supported. Eligibility and active state are derived inside the provider, not guessed from native DOM tags by the host. The session and workspace keep existing readonly, save/review, optimistic-conflict and lifecycle locks; forced clicks cannot bypass those checks. A replacement engine can implement the same action without exposing a vendor selection or command.

This is separate from the insertion picker's **İlk satır başlık olsun** choice, which configures a new table. The new toolbar action changes an existing table, including one created without headings.

## Compatibility and page layout

No saved vocabulary, schema, server contract, renderer, font, package or database migration changes are necessary. The content remains schema 5, the renderer remains v15, and earlier saved revisions/PDFs are not overwritten. Explicit saves follow the existing immutable revision policy. API authorization, tenant isolation, subscription gating, review binding and signature preparation remain unchanged.

An all-heading first row already forms the repeated heading in the existing page planner and PDF renderer. Changing cell types triggers ordinary document-change measurement, whose fingerprint includes cell types. Enabling/disabling the first-row heading therefore reflows the editor and newly rendered PDFs using the same existing rules. A mixed or ordinary first row does not repeat. Explicit bold/size/color/highlight inside the cells is retained when changing their semantic type, so turning a heading off does not promise to remove independently applied bold text.

Merged cells, arbitrary row/column headings, multiple repeated rows, column widths, cell styling and oversized-row policy remain outside this slice.

## Verification

Pure ProseMirror tests cover first-row targeting from later rows, mixed rows, attributes/marks/content preservation, unchanged later heading cells/other tables, single-table text/cell selections, blocked outside/cross-table/node selections, 1x1 and 50x12 tables, schema compatibility and independent undo/redo. Session tests use a replacement provider to verify command dispatch and locks.

Two production-bundle browser scenarios check actual toolbar state, rich Persian/Turkish cell DOM preservation, selection, undo/redo, save/reload, retry/review/conflict locks and forced readonly events. The same 42-row existing table is rendered in mixed, heading and ordinary modes, while toggling inside the live paged editor. All 126 marker pages must match their PDFs, the full heading must repeat on every occupied table page, and the mixed/ordinary first row must appear once. PDF pages and the paged editor screenshot are visually inspected. Tests use a temporary loopback host and synthetic data, without AppHost or the existing SQL databases.
