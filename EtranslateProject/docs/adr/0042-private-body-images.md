# ADR 0042: Private document images and bounded image presentation

Date: 2026-10-28

Status: Accepted for this development slice

## Ownership and persistence

Body images belong to a translation document, independently of its pinned template and source-file collection. `document_images` stores immutable metadata and a private blob key, SHA-256, uploader and upload time. A new migration only creates this table, its foreign key and indexes; it does not rewrite drafts, template content, PDFs or review bindings. Template assets retain their existing ownership rules. No package or paid service is added.

The document-scoped upload/download endpoints use the existing tenant authorization and subscription write filter. Upload also requires the current draft revision, an editable review state and a document rowversion write. A concurrent save/review fails closed, rolls back metadata/outbox changes and removes the just-created blob. Upload publishes `DocumentImageUploadedV1` and does not create a draft revision. Source files and their review snapshot are unaffected.

PNG/JPEG files are bounded to 5 MiB, a maximum edge of 10,000 pixels and 16 million decoded pixels. A bounded PNG IHDR/JPEG SOF reader checks dimensions before codec use; the blob store checks signatures. This is not a full image-codec validator. The browser must also decode the private response before inserting a node. Unsupported/corrupt inputs leave the document unchanged. PDF generation verifies ownership, size, signature and stored SHA-256 before decoding, retaining the existing ten-unique-image/aggregate resource limits.

Both API and BFF derive document ownership from tenant/job/document relationships. Private image responses use `private, no-store`; SVG and user-supplied URLs/data URLs are not document image inputs. Deleting a node does not delete its blob, so historical revisions remain renderable. Unreferenced-upload retention/garbage collection is a separate storage concern; this slice adds no destructive cleanup endpoint.

## Versioned content and rendering

Rich schema 9 adds nullable `assetScope` (`document`), `widthPercent` (integer 5..100) and `align` (left/center/right) attributes to body images. An absent/null scope retains template ownership. Old schemas 1..8 retain their strict vocabulary; legacy template images keep their old dimensions until explicitly resized. Document references are resolved only from the current document; all remaining asset references must belong to the pinned/target template. Changing a template therefore preserves owned body images without granting access to another document's images.

The provider and explicit rich saves write v9. Initial mount, selection, viewing and panel changes do not rewrite stored JSON. Raw saves inherit authoritative metadata, plain fallback preserves known versions through v9 and unknown v10 remains unsupported. The template form still writes its v1 contract. Old PDF identities/hashes and review bindings remain immutable, and the signing appendix remains v2.

Editor, preview and PDF use proportional width and automatic height with explicit physical left/center/right margins. A newly uploaded image is a separate top-level block at 50% width, centered. Floating text wrap, cropping, pixel dragging, inline text images and insertion into tables/lists/quotes are outside the UI contract. Existing supported historical image nodes remain readable. An image taller than printable content produces an explicit PDF error and paginated-view fallback; it is not silently clipped. Private image load/error events remeasure the live flow without serializing page coordinates.

The renderer advances to `draft-html-v19-body-images-pw1.63.0-noto-pdfsharp6.2.4`. Thin-pixel rounding by Chromium is allowed in geometry comparison; source pixel dimensions, aspect, alignment, marker pages and image pages are tested separately.

Preview rendering reuses one decoded private image element per URL/occurrence across text edits, pruning unused entries. This avoids downloading no-store files on every keystroke while keeping HTTP responses private. Failed preview images retain an explicit placeholder until reload/source change. Live page measurement retains the earlier request-free image measurement boxes.

## Replaceable editor boundary and asynchronous upload

Application commands are `insertImage`, `imageLayout` and `deleteImage`. `captureImageInsertion` returns a bounded opaque token, and `releaseImageInsertion` releases it. Only the provider handles the native selection/bookmark and maps it across transactions; native objects/HTML never cross into the host. One pending insertion is supported at a time. Text selections and nested insertion points are rejected, preventing accidental replacement of selected text. A paragraph is split around the block without losing its text marks, and a final editable paragraph is provided when needed.

While uploading, rich edits, saving and review submission wait. Readonly navigation can continue, but the captured insertion location remains authoritative. HTTP 409 keeps the session locked; retryable failures restore prior edit eligibility without relabeling the source. Every insert/layout/delete is a bounded transaction with independent Undo/Redo; deleting an image is reversible without uploading again. Session/version/readonly locks also guard forced UI events.

The collapsed Turkish **Metne görsel ekle ve düzenle** panel offers upload, integer percentage width, alignment and removal. Selecting an old image reports its default presentation rather than falsely labeling it 50%/center. Applying a new presentation is explicit. The document source, preview and saved PDF share the same bounded declarations.

## Verification

Shared Node/C# cases cover legacy defaults, nullable fields, exact bounds, malformed types, arbitrary URLs and unknown attributes. Model/session cases cover captured insertion after navigation, marked text preservation, history, invalid selections, the image-count limit, typed input and replacement-provider/readonly gating. BFF tests exercise a real v9 image payload through rich-save validation.

Production-bundle browser cases cover upload failure/retry/conflict, selection anchoring, resizing/alignment/deletion, save/reload, readonly locks and tall/missing-image fallback. Separate text-only and table fixtures compare 60 text markers and six image placements against actual PDF image bounds, pixel dimensions and aspect. A typing/Undo case verifies preview element identity and unchanged private-image request count. UI and representative printed pages were visually inspected.

Isolated SQL acceptance uses actual PNG/JPEG uploads through the Web/BFF, saved v9 JSON and PDFs. It checks unauthenticated/wrong-tenant/wrong-document access, a forged foreign-image draft, invalid bytes, stale upload revisions, review lock, template replacement and historical image printing. The archive case retains its previous PDF/review identity. The migration is exercised only on six fresh QA databases with guarded cleanup, not the user's existing database. See the [Persian guide](../testing/editor-body-images.fa.md) for commands and final execution results.
