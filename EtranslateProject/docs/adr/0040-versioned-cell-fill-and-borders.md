# ADR 0040: Versioned table-cell background and borders

Date: 2026-10-28

Status: Accepted for this development slice

## Contract

Rich schema 7 adds four optional nullable attributes on `tableCell` and `tableHeader`: `backgroundColor` and `borderColor` are exact seven-character #RRGGBB strings, `borderWidthPt` is 0, 0.5, 1, 1.5, 2 or 3, and `borderStyle` is solid/dashed/dotted. Null or absent uses the existing default for that cell type. A white fill can explicitly override the default gray heading; resetting a heading returns to its type's default background. Old schemas 1..6 reject the new fields, including null. No CSS string, image, alpha color, arbitrary edge map or vendor cell styling is accepted. Existing width, span, nesting and document-size limits remain.

C# and JavaScript use shared fixtures for exact values, newlines/injections and unsupported properties. Only explicit edits/rich saves export v7. Initial mount, scope/selection changes and inspection do not relabel old data. Raw saves retain authoritative metadata and plain fallback preserves known versions through v7. Unknown v8 is readonly/rejected; downgrade guards and immutable revision/PDF binding remain. No database migration, package, font or external service is added. The template form still writes v1.

## Editor action

The application-owned `tableCellStyle` command takes exactly `{scope, field, value}`. Scope is selection or table; field is one of the four attributes, or reset with null. Updating one field preserves the other fields. Reset clears only these four attributes, leaving text marks, paragraph settings, headings, cell contents and table widths intact.

The provider gathers the containing cell at a caret, the intersected cells of a text selection, or the distinct cells of a native rectangular cell selection. Table scope targets every cell in the same selected top-level table. Selections outside a table or spanning multiple tables fail closed. The host receives copied aggregate values, per-field mixed indicators, cell counts and custom-state flags, without a vendor selection or native object.

The collapsed Turkish **Hücre rengi ve kenarlık** panel offers both scopes, standard colors and native custom color inputs, bounded width/pattern selectors, mixed values and reset. Scope changes only inspect content; field changes apply immediately. Each command is one transaction with history boundaries before and after; a no-op does not dirty the document. The transaction preflights JSON size before dispatch so a large bulk style does not fail after replacing the host's last valid source. Existing session/provider, readonly, save/review and conflict locks remain in force, including forced UI events.

## Rendering and border policy

The Tiptap cell/header extensions carry only the application attributes and render safe CSS via DOM style properties. Managed clipboard HTML records only the bounded style fields. The body preview and PDF use the same declarations. The row planner's stale-layout fingerprint now includes the four inline paint properties; geometry is still measured independently on client/server, and no client print coordinates are trusted.

All four cell edges share one color, width and pattern in this slice. Collapsed borders follow Chromium's shared-edge rules: a stronger/wider neighbouring border can win. Explicit width 0 derives `border-style:hidden`, which suppresses shared edges as well; hidden is not a saved user style token. Choosing a nonzero width restores the chosen/default line pattern. Styling a header does not change which row repeats, and repeated headers retain their paint.

Widths are nominal CSS points. Chromium can quantize thin borders to device pixels before printing: the fixture's declared 2pt dotted border computes and paints at 1.5pt in the installed browser. Verification compares actual computed editor width with actual PDF geometry rather than asserting an unsupported physical precision. This limit is stated in the UI help and test guide. PDF fill/background painting is tested separately from text extraction.

The renderer advances to `draft-html-v17-cell-style-pw1.63.0-noto-pdfsharp6.2.4`; existing archived artifacts/review bindings remain unchanged and the signing appendix remains v2. Default cells produce the prior prepared HTML when the new fields are absent/null. Per-edge editing, merged cells, gradients, images and Word fidelity remain outside this slice.

## Verification

Shared Node/C# cases test bounded styles and old-schema rejection, typed action input, exact scope, mixed state, single/rectangular/range selections, content/selection/width preservation, no-op reset and immutable upgrade/downgrade. Production-bundle browser cases cover custom colors, bulk/single edits, history, save/reload, locks, style reset and preserving column percentages.

A saved multi-page LTR/RTL fixture checks 71 marker pages, repeated header fills, actual background paint, dashed/dotted color and thickness, and absence of borders for the zero-width cell. The computed editor widths are recorded with the marker report. PDF pages and UI are visually inspected. Isolated SQL acceptance adds persisted fill/border attributes to the existing archive case, preserving widths, old PDF hashes and review bindings while using only six fresh QA databases and cleanup guards.
