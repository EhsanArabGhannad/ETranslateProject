# ADR 0035: Local literal search, replacement and document counts

Date: 2026-10-27

## Scope and contract

Add body-only find/replace and document statistics to the existing rich editor. Content remains `etranslate.rich-document / 4`; draft renderer remains `draft-html-v14-inline-scripts-pw1.63.0-noto-pdfsharp6.2.4`. This adds no schema vocabulary, content migration, dependency, external service or database change. Existing immutable revisions, templates, PDFs and review references retain their identities.

The session exposes optional engine-neutral methods `setSearch`, `getSearchState`, `findMatch`, `replaceSearch`. Older replacement providers without these methods report search unavailable while retaining their existing editing contract. Search/options, match positions and decorations are transient provider state. Native positions, ProseMirror objects and selection APIs stay in `editors/tiptap-search.js`; the host only handles controls and plain state. Statistics use the application's JSON vocabulary and do not require a vendor API.

## Matching and replacement

Search is literal, non-overlapping and Unicode-aware. There is no regular-expression or HTML interpretation. Optional case sensitivity and whole-word matching use Turkish casing and `Intl.Segmenter('tr')` word boundaries; original UTF-16 positions are retained. Query length is at most 200 UTF-16 units and replacement at most 2,000, both single-line and well formed. No Unicode normalization, Persian/Arabic character substitution or transliteration is performed.

Adjacent text nodes in a text block are searched together even when their marks differ. Hard breaks, block and cell boundaries separate search segments. Images, template header/footer and generated page numbers are excluded. A maximum of 2,000 matches are decorated; an explicit `2.000+` message explains truncation and disables replace-all. The provider also rejects forced replace-all calls while truncated, so it cannot silently replace only the displayed subset.

Replacement retains the first matched character's marks for inserted text. Surrounding text, block direction, paragraph attributes, table structure and other content are preserved. This policy is visible in the panel, including when a match crosses different inline styles. Empty replacement deletes the matched text. Literal `$&` or `<b>` remains text. Replacements are applied from the end backwards in one transaction; history is closed before and after the operation. No-op replacements create no document/history steps. Schema, serialized JSON size and extracted plain-text size are checked before dispatch; an oversized result is rejected without freezing the editor or partially applying changes.

Search/next/previous/counting are available in read-only content and during temporary write locks. Replacement is guarded at UI, session and provider levels, including forced disabled-button events. An immutable read-only session cannot be upgraded by a retry handler. The readonly editor has a focusable DOM selection for navigation/copy, without becoming editable. Search navigation selects the match, scrolls to it and follows the existing paginated-view page tracking. Opening/searching/navigating does not dirty JSON or add undo history.

## Statistics and interaction

The counter includes all body paragraphs/headings, nested lists and table cells exactly once. Words are `Intl.Segmenter` word-like segments, including numbers. Characters are grapheme clusters, so a combining accent or family emoji is one character. A second total excludes whitespace. Hard-break and generated block-separator newline characters are excluded from both character totals; ordinary spaces count in the inclusive total. Formatting, assets, letterheads and generated page furniture do not count. Counts update from the working JSON after edits/undo/redo; native selection changes do not change the document totals.

The Turkish panel is collapsed by default, with labels and live match feedback. Ctrl+F/Ctrl+H open it only from the rich-editor region. Enter in the query goes forward; Shift+Enter goes back; Enter in replacement applies one change. These events do not submit the surrounding save form. Query typing is debounced and IME composition completes before matching. Escape inside an input closes the panel and returns focus to the editor. Query state and counts are local to the tab and are recomputed after reload.

## Verification and boundaries

See the [Persian test guide](../testing/editor-search-replace.fa.md). Unit cases cover Turkish case pairs, Persian joiners, literal metacharacters, Unicode offsets/graphemes, limits, boundaries, statistics and session locks. Product-bundle browser tests cover cross-mark replacement, first-character formatting, no-op/delete/literal insertion, replace-all history, count restoration, current-page navigation, save/reload/retry, readonly navigation and forced calls beyond the match/input limits. The isolated SQL product test replaces text through Web, verifies persistence, prints the saved replacement and retains archived PDF/review identity. Existing browser/PDF geometry checks run as regression coverage.

This step does not add regex, cross-block replacement, header editing, selection-only counts, a replacement audit log or Word import. Existing database deployment and broad role/subscription/browser acceptance remain separate work.
