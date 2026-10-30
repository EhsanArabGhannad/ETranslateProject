# ADR 0045: Native Word clipboard corpus and bounded acceptance

Date: 2026-10-29

Status: Accepted for the captured Windows Word/Chrome combination

## Decision

Exercise the existing clipboard boundary with three generated, non-personal DOCX documents copied in native Microsoft Word. Retain sanitized clipboard HTML/text and provenance as regression fixtures, rather than treating synthetic Office HTML as evidence of native Word behavior. Keep the free editor, application schema v10, renderer v20 and existing dependencies. No migration or stored-document conversion is needed.

The installed Office executable reports product version `16.0.20527.20088`; the documents opened in Compatibility Mode, and the clipboard HTML generator reports `Microsoft Word 15`. These describe different metadata and do not establish a test matrix across Word versions. The original receiving browser was Chrome 156 on Windows, in an independent temporary profile. All three original paste events had `isTrusted=true`. Automated regression replays those captured payloads using synthetic events; the replay itself is not labelled a native clipboard event.

## Product corrections

- Normalize incoming HTML direction values before checking the allowlist. Word's uppercase `dir=RTL` now retains paragraph direction.
- Treat cells inside a semantic `thead` as table headers even when Word emits `td`. This retains heading repetition in the existing page-flow/print contract.
- Ignore empty Office `o:p` placeholders containing only whitespace/NBSP, while retaining the containing empty paragraph. Nonempty Office text is still imported.
- Report an inline direction override that cannot be represented by the paragraph-level direction vocabulary. This does not add inline bidi-isolation support.

## Corpus and repeatability

The source builder is `tests/ETranslate.EndToEndTests/create_word_clipboard_corpus.py`. Typography covers Turkish/Persian Unicode, ZWNJ, literal markup/currency, inline styles, empty paragraphs and an explicit page start. Tables cover lists, 27 rows, repeated header semantics, fills and a horizontal merge. Unsupported features cover a generated image with alternate text, a vertical merge and a synthetic hyperlink.

Raw captures and source DOCX files stay in ignored `tmp/`. `curate_word_clipboard_corpus.py` replaces machine-local file URLs and retains the original HTML hash, clipboard formats and capture provenance. The committed JSON fixtures and manifest contain generated test content only. Product documents and clipboard reports are not newly archived by this feature.

The opt-in local capture harness opens a separate headed Chrome profile and accepts only the three QA case names and marker-bearing native events. Word selection/copy remains an explicit interactive step; the harness does not read a background clipboard. Normal tests skip this interactive host. The deterministic replay is included in the existing browser verification script. See the [Persian guide](../testing/editor-native-word.fa.md) for execution and results.

## Acceptance and limits

All three fixtures pass strict v10 validation, marker preservation, single-step Undo/Redo, save/reload, native page-flow and production PDF comparison. The PDF corpus contains seven pages and 79 distinct markers. Heading duplication is allowed only for the two repeated table headings. The isolated SQL acceptance also replays the captured typography through the actual product save/reload/download path while retaining existing archive, private-image and review checks.

The source Word page counts were 2/3/1; product editor/PDF counts are 2/4/1. Acceptance means the converted application document agrees with its own PDF, not that Word pagination or font metrics are reproduced. External images still require private upload; vertical merges flatten with feedback; links retain visible text only. Row-level style inheritance, exact Word fonts, inline bidi runs, DOCX import, tracked changes, floating objects, fields and broader OS/browser/Word combinations remain separate work.

Native Word was used to inspect the source documents. The bundled DOCX-to-PNG renderer could not run on this Windows host because its LibreOffice executable is absent; no successful source-render claim is made. All seven product PDF pages were rendered and visually reviewed, in addition to automated comparisons.
