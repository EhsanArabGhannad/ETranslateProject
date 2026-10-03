# ADR 0007: Immutable template images and pinned document layouts

- Status: Accepted
- Date: 2026-10-04

## Decision

- Template images belong to one template and inherit its tenant authorization. Assets have immutable IDs, storage keys, metadata and SHA-256 digests. There is no overwrite or delete API.
- Uploads accept PNG or JPEG up to 5 MiB and check the declared MIME type against the binary signature through the existing blob store. Full image decoding and malware scanning are outside this storage slice.
- Editor JSON refers to managed images with `assetId` GUIDs. Every such reference is checked against assets owned by that template when a revision is saved. To add a logo, create the template, upload the image, then create a revision containing its ID.
- A document can apply one active template revision before its first draft save. Applying it atomically pins `TemplateRevisionId`, creates draft revision 1 from the template body, and publishes outbox events.
- Later template revisions and archival do not change the document's pinned header, footer, watermark or page layout. Later document drafts can reference images of the pinned template.
- The document revision counter is an EF concurrency token. Competing draft saves and template application return HTTP 409 instead of silently overwriting work.
- Foreign keys restrict removal of referenced template revisions and assets' parent templates.

## Consequences

Historical assets remain downloadable by authorized tenant members after archival. Existing documents without a selected template continue to work. Template selection for documents that already contain editor revisions, changing templates, rendering and the visual editor will require separate workflows. Rendering must resolve managed asset IDs through storage and must never fetch arbitrary image URLs from editor JSON.
