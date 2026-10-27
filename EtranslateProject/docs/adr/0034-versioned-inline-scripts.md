# ADR 0034: Versioned superscript, subscript and strike controls

Date: 2026-10-27

## Content contract

Introduce `etranslate.rich-document / 4`. It retains the v3 block vocabulary and typography, and adds two attribute-free text marks: `superscript` and `subscript`. They are mutually exclusive on each text node. Duplicate marks, additional properties, attributes (even an empty object), conflicting scripts and unknown marks are rejected by both validators. The shared JSON fixture supplies valid and invalid cases to client/server tests. The existing attribute-free `strike` mark remains valid in legacy/v1/v2/v3; this step exposes its command in the toolbar.

Stored characters remain ordinary Unicode text: the script is formatting, not replacement with a superscript numeral character. Plain-text extraction keeps those characters. Current rich-editor saves create v4 revisions. Mounting or paging an older revision does not rewrite its JSON or descriptor. Raw saves inherit authoritative metadata; plain-text fallback preserves the known current version. V4 cannot downgrade to v1/v2/v3, and future v5 remains unsupported. The template form still writes v1. No database migration, new dependency, licensed SDK or external service is introduced.

## Provider and rendering

Commands pass through the existing engine-neutral session. A provider declaring a version before v4 cannot issue script commands. The Tiptap provider defines small application marks, exclusive of each other, rather than leaking vendor types into the host. Clicking an active script clears it; selecting the opposite script removes the previous script and applies the chosen one in a single history transaction. The same behavior applies to stored marks at the caret. Tiptap's generic `can().setMark` refuses an excluding mark, so script eligibility checks the containing text blocks and the command explicitly removes the opposite mark before toggling. Existing read-only, retry/conflict, review and historical locks still apply.

Script size is 75% of the selected font size, with zero line height, relative positioning and baseline alignment. Superscript moves up by 0.5 of its own em; subscript moves down by 0.25. These are fixed application styles, never arbitrary content CSS. DOM construction uses CSSOM. Provider nesting is typography outside script outside ordinary marks. V4 preview/PDF use the same semantic wrapping order independently of input array order. This makes both font-size reduction and strike color/position inherit correctly. V1/v2/v3 retain their original mark wrapping order and HTML behavior.

New draft renderer identity: `draft-html-v14-inline-scripts-pw1.63.0-noto-pdfsharp6.2.4`. V4 uses the packaged typography fonts. Archived PDF bytes, IDs, hashes and review bindings are unchanged; a new renderer cache identity creates a separate new artifact. The signing appendix keeps `signing-preview-v2-letterhead` and its established font path. Page flow and the independently calculated table row plan are unchanged.

## Verification and limits

See the [Persian test guide](../testing/editor-inline-scripts.fa.md). Browser coverage includes partial Latin/Persian selections, switching and clearing scripts, caret typing, mixed selections, tables, typography reset, history, saved v4 reload, preview, page view, failed save and forced read-only events. The PDF verifier inspects actual embedded glyph sizes and baseline offsets and same-color strike vectors, including a script in a table. The real SQL harness saves a styled script suffix in a table and checks persisted JSON, PDF geometry, page markers, archived file identity and review locks. It only creates fresh owned QA databases.

This adds text-formatting controls, not equations, footnotes, arbitrary baseline offsets, Word import or a full text-format reset. The existing typography reset retains script/strike/bold/italic/underline. Existing database deployment and the wider role/subscription/browser acceptance matrix remain separate work.
