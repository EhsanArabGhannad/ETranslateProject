# ADR 0041: Versioned horizontal table-cell merge and split

Date: 2026-10-28

Status: Accepted for this development slice

## Contract

Rich schema 8 permits integer `colspan` values 1..12 on table cells and headers. `rowspan` remains 1 and vendor pixel widths remain null. The sum of spans in each row must equal the table's logical column count, bounded to 12. Table percentages address those logical columns, not the number of physical cells in the first row. A full-width merged header therefore does not erase or shorten the percentage array. The limits of 50 rows, top-level tables, content types, nesting, JSON size and node budget remain in force. Schemas 1..7 retain their earlier span policy; shared JavaScript/C# fixtures cover both acceptance and rejection.

The provider and explicit rich saves now write v8. Mounting, selecting, searching or opening a panel does not rewrite historical source JSON. Raw saves inherit authoritative metadata; plain fallback preserves known versions through v8. Unknown v9 remains unsupported and downgrade guards still apply. No database migration, package, font, paid component or external service is added. The template form continues to write its existing v1 contract.

## Commands and preservation

The application-owned commands are `mergeNextCell`, `mergeCellsInRow` and `splitCellInRow`. Their adapter boundary exposes enabled state, not a vendor selection. `horizontalCellMerge` is advertised separately while the broader `mergedCells` capability remains false: vertical and general rectangular merges are outside this contract.

Merge accepts adjacent physical cells in one row of the same selected top-level table. They must have the same cell/header type, direction, marks and cell appearance; only their current colspan may differ. Text selections and native cell selections use the existing selected-cell resolver. Cross-row, cross-table, outside-table, differently styled or mixed header/body selections fail closed. The next-cell action uses document order, which is visually leftward in an RTL table.

The merged cell sums the original spans and concatenates all original child blocks in order. Empty paragraphs, text marks, paragraph settings and contents are retained. Splitting creates one ordinary-width cell for each logical column: all content stays in the first cell and the others are empty, with the same cell attributes/type. Undo restores the original distribution; split does not guess which text belonged to a pre-merge cell. Each action has history boundaries before and after and preflights schema, text and JSON limits before dispatch.

Existing session, readonly, save/review and conflict locks remain in force, including forced button events. Deleting a column while the selection intersects a merged cell is disabled because a colspan covers several logical columns. Users can select an ordinary cell in that column or split first. Native row/column insertion and deletion otherwise remain available within the validated grid limits. Changing the logical number of columns resets custom percentages as before; merge/split does not change that number or reset the percentages.

## UI and rendering

The collapsed Turkish panel **Aynı satırda hücre birleştirme** explains adjacency, matching appearance, RTL order, split content placement and the column-delete restriction. The controls use the existing generic command pipeline and retain the editor's selection.

Both cell renderers, the body preview and the PDF emit bounded horizontal spans. The shared row planner accepts only equal logical widths across rows, still rejects vertical spans/nested tables and includes non-default spans in its stale-layout fingerprint. Default span-one cells retain the earlier fingerprint and prepared HTML. All-header first rows still repeat, including merged headers. Client and server independently measure geometry; no stored page nodes or trusted client coordinates are introduced.

The PDF renderer advances to `draft-html-v18-horizontal-spans-pw1.63.0-noto-pdfsharp6.2.4`. Archived PDF IDs/hashes and review bindings remain immutable; the signing appendix stays v2. A merged row taller than printable content retains the existing explicit rejection/fallback policy, rather than silently clipping text.

## Verification

Shared cases cover partial/full spans, equal and custom widths, numeric integer forms, inconsistent grids, limits, native widths and vertical spans. Model/session tests cover text/style preservation, repeated merge/split, exact Undo/Redo, text/rectangular selections, header toggles, row/column actions and old-provider command gating.

Production-bundle browser tests cover real controls, retry/reload, readonly locks and persistence. A saved eight-page fixture compares all 73 marker pages and actual PDF cell boundaries: partial 80/20, full-width spans, RTL 1/3 versus 2/3 and default three equal columns. Styled and unstyled merged headers are included; the long header repeats on pages 1..5. UI and representative PDF pages were visually inspected.

The isolated SQL archive case persists schema 8, a horizontal merge, fill/border settings and 35/65 logical widths through the actual Web/BFF/services, retaining the archived PDF and review binding. Only six fresh QA databases are used and removed. See the [Persian test guide](../testing/editor-horizontal-merge.fa.md) for the executed results and user steps.
