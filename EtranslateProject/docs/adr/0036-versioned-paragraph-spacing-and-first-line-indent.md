# ADR 0036: Versioned paragraph spacing and first-line indentation

Date: 2026-10-27

## Vocabulary and compatibility

Introduce `etranslate.rich-document / 5`. It retains v4 and adds optional paragraph/heading attributes:

| Attribute | Values |
| --- | --- |
| `spaceBeforePt` | integer-valued number 0–72, or NULL |
| `spaceAfterPt` | integer-valued number 0–72, or NULL |
| `firstLineIndentMm` | number 0–40 in 0.5 mm increments, or NULL |

NULL/omitted values retain the established defaults. Explicit zero removes that spacing/indent. There is no arbitrary CSS, unit string, negative/hanging indent or page-layout change. Shared client/server fixtures define the bounded values. Versions 1–4 and NULL/NULL do not accept the new properties, including NULL values. The base `DocumentParagraph.ValidValue`/client helper default remains v3; versioned validators and current provider explicitly request v5.

A current rich-editor save creates a new v5 revision. Merely mounting, selecting, searching, paging or zooming an older revision does not rewrite its content or metadata. Raw saves inherit authoritative metadata; plain-text fallback preserves known v5. Downgrades are rejected and future v6 stays unsupported/read-only. The template form still writes v1. No new database column, migration, font, library, license or external service is added.

## Layout semantics and UI

Bounded values map to `margin-top`/`margin-bottom` in points and `text-indent` in millimeters. First-line indentation follows the paragraph's resolved direction; the existing logical block indent still affects every line. An RTL paragraph indents its first line from the right, LTR from the left. Physical alignment, direction, inline typography and scripts remain independent. Ordinary CSS margin collapsing applies: adjacent 12pt-after/6pt-before margins yield a 12pt gap, not their sum. Margins may be discarded at automatic page breaks according to Chromium fragmentation rules. No promise of retained blank space at every page start is made.

The Turkish controls live in the collapsed **Aralık ve ilk satır** section. Common choices include explicit zero and 12.5 mm; valid stored values outside the initial choice list use the existing custom-value display. Commands and mixed selection state pass through the engine-neutral session. Multiple blocks and table cells are supported. Paragraph/heading conversion retains all settings. Paragraph reset now clears all six paragraph attributes and preserves text, direction and inline marks. Read-only, review, save/retry and conflict locks still apply.

DOM styles are assigned through CSSOM; PDF styles are bounded and HTML encoded after descriptor validation. Continuous/paged views and approximate preview share the style mapping. The provider exports v5, and the search replacement preflight validates that current vocabulary while retaining its existing size guards. The renderer independently measures the shared table row plan; it never accepts a client's page coordinates as print authority.

## Print and verification

New draft renderer identity: `draft-html-v15-paragraph-spacing-pw1.63.0-noto-pdfsharp6.2.4`. Known v5 body/template descriptors use the already packaged typography fonts. Without new settings, prepared v4/v5 HTML is identical. Existing PDF bytes, IDs, hashes and review references are preserved; renderer cache identities remain separate. The signing appendix keeps its existing `signing-preview-v2-letterhead` identity and base-font path.

See the [Persian test guide](../testing/editor-paragraph-spacing.fa.md). Shared cases test bounds and version strictness; aggregate/BFF checks cover upgrade/inheritance and downgrade rejection. The browser scenario covers mixed blocks, reset, heading conversion, search replacement, history, saved reload and locks. A five-page synthetic fixture compares every marker's editor/PDF page and measures real glyph coordinates: before/after gaps, LTR/RTL first-line displacement, unchanged later-line block indentation, cell indentation and margin collapsing. The isolated SQL product harness saves the new attributes inside a styled table paragraph, reloads them through the API and prints via the real services while retaining archived PDF/review identity.

The controls apply to the translation body. This is not a template-designer extension, hanging-indent/ruler implementation or a guarantee of faithful Word layout/import. Deployment against existing customer databases and the broader browser/role/subscription matrix remain separate work.
