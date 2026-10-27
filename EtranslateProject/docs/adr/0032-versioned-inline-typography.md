# ADR 0032: Versioned inline typography with local fonts

Date: 2026-10-27

Continuation: this ADR records the v2 typography step. [ADR 0033](0033-versioned-paragraph-layout.md) adds v3 paragraph layout and renderer v13; current rich-editor saves use v3. V2 remains supported, and the next unknown version is v4. Historical boundaries and verification below describe the original v2 step.

## Scope and content contract

Add font family, integer point size, text color, highlight and a reset for these four properties. Controls operate on the native editor selection or stored marks at the caret. They do not expose a vendor instance to the host. Mixed selection is represented in the toolbar; changing one field preserves the other properties on each text range. Reset removes the typography mark while preserving bold/italic/underline, direction, text and structural nodes. Each typography command starts a separate history group. Save/review/historical locks apply to all new controls through the existing session.

Introduce `etranslate.rich-document / 2`. V1 and NULL/NULL retain their existing strict vocabulary; neither accepts the new mark during interpretation. V2 extends only text marks with:

```json
{"type":"typography","attrs":{"fontFamily":"serif","fontSize":18,"color":"#b91c1c","highlight":"#fef08a"}}
```

Supported family tokens: `sans`, `serif`, `arabic`. Point size is an integer 6–72. Colors are exactly six hex digits prefixed by `#`. Each property can be omitted or NULL; at least one non-NULL property is required. Unknown properties, URLs, CSS strings, empty marks and duplicate mark types are rejected. Client/server cases share the same fixture. Paragraph/page/table vocabulary is unchanged. No metadata envelope, database column or migration is added.

The provider's current export format is v2, with a separate authoritative source format for validating a mounted legacy/v1/v2 revision. It must preserve that source before editing. An explicit rich-editor save creates a new v2 revision; mounting, selection, zoom, pagination or changing a template does not rewrite old JSON or descriptor rows. Raw saves inherit their authoritative revision descriptor. Omitted metadata on a v2 aggregate inherits v2; explicit v2-to-v1 downgrade is rejected. Future v3 is unknown and remains read-only. The old v1 writer constant and template form remain v1, allowing simple template operations to preserve their established contract.

## Rendering and resources

`DocumentTypography` and the small engine-neutral client module map the bounded values to CSS. The editor mark renders a DOM span using CSSOM, with application data attributes for copying its supported representation; it does not inject arbitrary style strings. The approximate preview uses the same mapping. PDF styles are emitted only after descriptor validation and HTML encoding. Runtime font/network URLs are not accepted from content.

Families are Noto Sans, Noto Serif, and Noto Sans Arabic, with deterministic fallback between the packaged families. Add the original Noto Serif variable normal/italic files and their SIL OFL notice; provenance and hashes are in `Rendering/Fonts/NotoSerif-sources.md`. Builds copy the local assets and notices; runtime does not download fonts. Paged view waits for the known families/weights/styles before measuring. Continuous view and paged view use the same inline values; page counting remains view-only.

New draft renderer identity: `draft-html-v12-typography-pw1.63.0-noto-pdfsharp6.2.4`. Font resources are extended only for v2 body/template inputs. The legacy/v1 base HTML/font path is retained; existing PDF files, IDs, SHA-256 values and review/signing references are not rewritten. The signing-preview appendix still uses its original base-font path and `signing-preview-v2-letterhead` identity. Original source PDFs are combined as archived bytes, not reinterpreted as text.

## Verification and boundaries

See the [Persian test guide](../testing/editor-typography.fa.md). Browser checks include independent Persian/Latin selections, mixed formatting, reset, history, saved v2 reload, retry/freeze and read-only control events under CSP. PDF inspection checks actual embedded Noto fonts, point size, text colors and highlight rectangles; raster inspection checks visual output. The SQL-backed product test applies the controls within a table, saves via Web, compares editor/PDF pages, and retains the old archived fixture and review reference. Only fresh isolated databases are used and removed.

This is not arbitrary font upload, faithful Word/DOCX import, paragraph alignment/spacing/indent, superscript/subscript or a general CSS editor. The UI provides explicit initial palettes and common sizes; unknown valid sizes/colors from v2 content are shown without discarding them. Typography in the template designer is not added here. Oversized table rows retain the existing explicit continuous fallback/print validation rather than being truncated. Existing customer database upgrade and deployment remain separate from the isolated acceptance test.
