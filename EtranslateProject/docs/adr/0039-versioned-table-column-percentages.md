# ADR 0039: Versioned percentage widths for table columns

Date: 2026-10-28

Status: Accepted for this development slice

## Contract and compatibility

Rich schema 6 extends schema 5 with the optional table attribute `columnWidths`: null/absent for equal columns, or an array of integer percentages from 1 through 100, exactly one entry per column and a total of exactly 100. A single column can be `[100]`; at most 12 columns remain supported. All rows must still be rectangular, with the existing 50-row/node/depth/text/JSON limits. The C# and JavaScript validators share test cases, accept integer-valued JSON numbers consistently, and reject the new attribute even when null in versions 1 through 5. Percentages are table-level application data, not vendor cell `colwidth` pixel arrays, CSS strings or arbitrary HTML. Cell spans and alignment restrictions remain unchanged.

The default provider reads/writes schema 6; only explicit document changes/saves export that version. Mounting, navigation, selecting a table and editing the pending size fields do not rewrite or relabel old source JSON. The BFF stamps rich saves with v6, raw saves inherit the authoritative descriptor, and text fallback preserves known versions through v6. Future v7 content fails closed, and monotonic version/downgrade guards remain in place. No storage schema migration, data backfill, package/font change or automatic archive conversion is needed. Template form writing remains v1.

## Editor command and interaction

The engine-neutral `tableColumnWidths` command takes an array or null. Its state exposes `columns`, a copied `value` and an opaque ephemeral `context` identifying the selected table revision. No native node, coordinates or selection leaks into the host. The session gates it to a provider supporting schema 6, validates count/sum/range and copies input before dispatch. The provider also validates it and requires a selection contained in one top-level table, using the same context policy as the first-row heading command.

The Turkish **Sütun genişlikleri** section displays one field per column, **Genişlikleri uygula** and **Eşit sütunlara dön**. The default equal distribution is shown as rounded integer percentages that sum to 100; they become a custom distribution only after Apply. Reset removes the custom array and returns to mathematically equal columns. Invalid pending values disable Apply and do not block saving existing document text. Enter in a field cannot submit the draft. Pending fields are rebuilt on a different table/revision so values cannot silently carry over to another table with the same number of columns. Repeated selection changes inside one table do not erase pending input.

Applying/resetting uses a single table-attribute transaction, preserves cell contents/marks/attributes/direction/selection and has history boundaries on both sides. No-op updates do not dirty the document. Editing rows and first-row headings preserves the array. Toolbar column insertion/deletion resets a distribution to null inside that same structural transaction; an explanatory UI note makes this policy explicit, and Undo restores both the previous dimensions and distribution. Other transformations producing a mismatched width-array length are rejected by the preservation filter rather than saved inconsistently.

Readonly, save/review, optimistic-conflict and disposal locks remain authoritative. Width controls follow the same session lock even during forced events. The original saved source descriptor remains distinct from the working-copy descriptor used for live previews.

## Rendering

Custom widths generate semantic `<colgroup><col>` with bounded percentage widths in the editor, body preview and PDF. Editor styles use the CSS DOM API under the existing CSP. The table remains 100% of the content width with fixed layout, so percentages adapt to page geometry and zoom. Array order is logical DOM-column order; a table's RTL direction places its first column on the right in both editor and PDF. The default null/absent path retains prior prepared HTML.

The shared row planner's stale-layout fingerprint includes the application width declaration. The planner still independently measures actual row geometry on client and server; no client print coordinates are trusted. Body/font/paragraph policies remain the same. The PDF renderer advances to `draft-html-v16-column-widths-pw1.63.0-noto-pdfsharp6.2.4`; old artifacts and exact review bindings remain unchanged, while new renders get a separate versioned artifact. The signing appendix renderer remains v2.

Very narrow columns can increase row height; existing explicit oversized-row fallback/rejection remains in effect. Drag handles, fractional percentages, merged cells, cell styling and Word import are outside this slice.

## Verification

Shared Node/C# cases cover the exact percentage/count/sum boundaries, null/absent compatibility, immutable upgrade/downgrade and rejected vendor/CSS/foreign attributes. ProseMirror and replacement-provider tests check content/selection preservation, no-ops, row/column mutation policy, typed dispatch and locks.

Production-bundle browser cases exercise pending fields, invalid totals, form isolation, saved v6, undo/redo/reset, structural edits, switching tables with identical counts, save retry and readonly behavior. Two saved LTR/RTL tables are compared across editor pages and PDF markers; a PDF parser measures actual border coordinates to verify 50/30/20 and 65/35 logical percentages, including RTL reversal. PDF pages and UI screenshots are visually inspected. SQL acceptance adds saved 35/65 widths to an existing isolated archive/review scenario. It uses six fresh QA databases and its cleanup guard; existing user databases are not started or migrated.
