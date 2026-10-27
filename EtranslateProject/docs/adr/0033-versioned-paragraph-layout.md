# ADR 0033: Versioned paragraph layout

Date: 2026-10-27

## Contract and compatibility

Introduce `etranslate.rich-document / 3`. It retains v2 typography and adds the following optional attributes to body paragraphs and headings:

| Attribute | Values |
| --- | --- |
| `textAlign` | `left`, `center`, `right`, `justify`, or NULL |
| `lineHeight` | numeric 1, 1.15, 1.5, 1.65, 2, 2.5, 3, or NULL |
| `indentLevel` | integer 0–8, or NULL; each step is 5 mm at the logical start |

Omitted/NULL values use the existing defaults. Start indentation follows each block's resolved direction; physical alignment does not change direction. No arbitrary CSS, URLs, negative indentation, first-line indentation or paragraph before/after spacing is introduced. Client/server fixtures share the bounded vocabulary. V1, v2 and NULL/NULL remain strict to their original vocabulary; even new NULL attributes are not silently accepted as an older version.

The provider exports v3 after an explicit rich-editor save. Mounting a legacy/v1/v2 revision, selecting, zooming or paging does not rewrite the authoritative source or its descriptor. Raw saves inherit their revision descriptor; text-mode reconstruction retains v3 on a v3 revision. Downgrades to older rich versions are rejected. Unknown v4 remains unsupported/read-only. The template designer continues to write v1. No migration, database column or content envelope is added.

## Editor and preview

Engine-neutral command names, values and state pass through the existing session. The provider applies block properties to the paragraphs/headings intersecting the native selection, including multiple cell ranges. A caret changes its containing block. Mixed selections display `Karma`; changing one property preserves the other properties of each block. Increase/decrease indentation clamps at 0–8. Reset removes these three attributes while retaining direction, inline typography, text and structure. Paragraph/heading conversion retains the properties. Changed commands start separate history groups; unchanged attributes do not create document steps. Existing retry/conflict, review, historical and read-only locks cover all controls.

The Tiptap provider extends the existing paragraph and heading nodes; the application contract does not depend on vendor node objects. Two existing transitive Tiptap 3.31.4 packages are declared direct dependencies; no new editor SDK or paid service is added. Supported internal clipboard attributes are parsed as bounded application data. This is not a Word paste implementation.

DOM styles use CSSOM through `paragraph.js`. The approximate preview and PDF use the same bounded mappings. The preview has separate ephemeral working-copy metadata after edits, because the mounted revision may still be v1/v2 while the current provider document is v3. Authoritative saved metadata is never relabelled for preview. Page changes remain view-only and do not insert page nodes in the content.

## Print and verification

New draft renderer identity: `draft-html-v13-paragraph-pw1.63.0-noto-pdfsharp6.2.4`. Body and template descriptor validation precede rendering; v3 uses the packaged typography fonts, and server-emitted styles are bounded and HTML encoded. The existing independent table row plan remains in use. Archived PDF bytes, identifiers, hashes and review references remain unchanged. The signing appendix retains `signing-preview-v2-letterhead` and its base-font path.

See the [Persian test guide](../testing/editor-paragraph.fa.md). Shared cases reject unsupported versions/values. Browser tests cover RTL/LTR, physical alignment, logical indentation, multiple blocks, mixed state, heading conversion, history, save/reload, paged view and locks. PDF checks compare marker pages and actual colored glyph geometry, including indentation, center position and line distances. The SQL product harness exercises the controls within a table through a real save and verifies the v3 attributes; it uses only fresh owned QA databases. Existing database deployment and the broader role/subscription/browser matrix remain separate work.
