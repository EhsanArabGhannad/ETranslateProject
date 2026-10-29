# ADR 0044: Bounded Word clipboard import with explicit conversion feedback

Date: 2026-10-29

Status: Accepted for the clipboard slice; real Word corpus and DOCX import remain separate

## Decision and boundaries

Keep the pinned free Tiptap packages. Add no package, licensed extension, service, migration, schema version or PDF renderer version. Imported content uses the existing application vocabulary v10; the renderer stays v20. Immutable revisions/PDFs are not rewritten. Mounting and changing paste mode do not change the hidden source or descriptor.

The application-owned `clipboard-import.js` converts clipboard HTML into allowlisted JSON, independently of a vendor. `clipboard-style.js` contains bounded color, unit, font, percentage and markup helpers. The Tiptap provider owns selection fitting, transactions, history and native clipboard events in `editors/tiptap-clipboard.js`. The host uses optional `getPasteState`/`setPasteMode` methods through the session; it receives allowlisted outcome/error/warning codes, never clipboard HTML or a vendor selection. A replacement provider can reuse the converter and must implement the transaction/clipboard boundary explicitly.

The Turkish **Word ve panodan yapıştırma** panel selects supported-format or plain-text mode. Ctrl+V uses that choice; Ctrl+Shift+V overrides only the current paste. The mode and conversion feedback are local to this editor mount. The feedback is outside the collapsed panel and uses textContent. No clipboard permission prompt or background clipboard reading is introduced.

## Conversion policy

Supported input includes bold/italic/underline/strike, super/subscript, bounded typography, headings, paragraph direction/alignment/spacing/indent, compatible explicit paragraph page starts, native lists and common Office `mso-list` paragraphs. Simple tag/class stylesheet selectors and inline CSS are read, with simple-selector specificity and inline precedence. Common Office font families map to the three bundled Noto families and report substitution. Sizes, lengths and line spacing map to existing bounded values and report approximation. CSS layout is not a general browser/Word rendering engine.

Office fragment comments are honored when present as one ordered StartFragment/EndFragment pair: retain ancestor context and stylesheet rules, delete surrounding content, and import the selected fragment. Ambiguous/malformed pairs reject the operation. The browser supplies text/html; raw Windows CF_HTML byte-offset headers are not a new supported file format.

Tables preserve compatible rows/cells, explicit header cells, horizontal colspan, column ratios, supported cell fill and uniform borders. They must satisfy the existing 50-row/12-logical-column rectangular contract, with no rowspan or nested table. Unsupported tables, or tables pasted into a nested destination, become ordered cell blocks with a visible warning. Captions become paragraphs and report adaptation. Word list levels beyond the implemented flat Office-list conversion report simplification; native nested lists are retained. Links retain their visible text and report that the link is removed. Page layout, unsupported dimensions, hidden runs and unsupported elements may be omitted or adapted with feedback. No fidelity percentage is claimed.

External clipboard images, embedded objects, remote/file/data URLs and forged asset IDs are not imported as private assets. Image alternate text is retained when available, and the user is directed to the existing private upload tool. A last-copy token and native slice held only by the current adapter allow copy/cut/paste inside that same mounted editor to preserve its managed images and exact supported fields. A missing/forged/foreign/expired token uses the bounded external path. This is not an interchange guarantee between editor mounts or tabs. Cut on readonly content can copy but cannot delete.

Plain mode prefers text/plain, retaining literal markup, Unicode, tabs and empty lines. If the clipboard has only HTML, it extracts the converted visible text. Plain mode deliberately discards source formatting. A single paragraph pasted inside nonempty text retains the destination paragraph layout and reports that context when relevant. Multi-block body paste preserves the surrounding text and keeps one existing start flag instead of cloning it onto the suffix. Range/all-document selections replace the explicit selection; CellSelection and unsupported resulting shapes reject without changing source.

## Isolation, limits and history

Clipboard HTML never enters live DOM. A bounded lexical pass renames style attributes before HTML parsing, because Chromium emits CSP violations for the original attributes even in the tested inert path. A detached document/template then provides structure and CSS declaration parsing. Only generated JSON reaches the editor; source scripts, links, handlers, style sheets and URLs never become executable/rendered DOM. The production CSP is unchanged. The tests assert no external requests or CSP errors.

Apply the existing 2,000,000-character JSON, 1,000,000-character extracted text, 20,000-node, depth-32 and manual-start limits to the result, and bound the raw clipboard/parser work as well. More than 500 supported stylesheet selectors rejects. Oversized/invalid/empty input and incompatible destinations return a report without changing content. Preflight the complete transaction before dispatch; a valid paste/cut is one independent undo step. Readonly, save, image-upload, review and conflict locks still apply in the provider, including forced DOM events. Server scope, ownership, revision and review validation remain authoritative.

Conversion warnings are transient UX feedback, not a stored provenance/audit report or a retained source Word document. A future DOCX/import workflow requiring those records must add explicit storage and review behavior. This slice does not implement DOCX, OLE, tracked changes, Word fields/footnotes, exact font metrics or a lossless Office interchange format.

## Verification

Node tests cover unit/color conversion, fonts, percentages, plain literal input/empty lines, lexical style handling, session/report filtering and lock/disposal, source limits, range replacement, forced-start preservation and undo. Production-bundle browser tests use synthetic Office markup, realistic clipboard data/events and the production CSP. They cover typography, paragraph settings, lists, tables/spans, selected-fragment context, nested destinations, hidden content, unsupported/hostile markup, forged local-copy tokens, private local images, readonly cut, save/reload and pending locks.

A saved sample produces a two-page PDF and compares 14 marker pages to the native editor, plus embedded Noto Serif, 18pt, red and yellow highlight. Positioned PDF glyph extraction handles variable-font text extraction artifacts without changing the product. Isolated SQL acceptance pastes a green 14pt serif run through the actual UI, persists/reloads it with the existing forced start, and verifies its downloaded PDF while retaining archive/image/review checks. See the [Persian test guide](../testing/editor-word-paste.fa.md) for executed counts and artifacts.

The samples are synthetic Office-style HTML, not a certified matrix of real Microsoft Word versions, OS clipboards or browsers. A curated non-personal real Word corpus and DOCX work remain before broader fidelity claims.

References consulted: [Microsoft HTML clipboard format](https://learn.microsoft.com/en-us/windows/win32/dataxchg/html-clipboard-format), [Tiptap JSON content insertion](https://tiptap.dev/docs/editor/api/commands/content/insert-content). Implementation also follows the installed ProseMirror clipboard/slice/history code rather than an unpinned external extension.
