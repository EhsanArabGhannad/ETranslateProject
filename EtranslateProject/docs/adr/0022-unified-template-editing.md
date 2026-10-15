# ADR 0022: Unified template creation, revision editing and managed images

Status: Accepted for the development slice

Date: 2026-10-15

## Decision

Step 2 follows ADR 0021. Settings/Templates selects a tenant-owned template or a new form. Selection loads the latest server revision into a typed form; existing names remain readonly because the current content-revision API does not rename metadata. No business API, service boundary, database schema or document pinning semantics change.

POST Settings/SaveTemplate re-reads live membership, subscription permission, selected template and current revision. Owner/Administrator/Translator may write; Reviewer, inactive templates and expired subscriptions cannot. Global antiforgery remains enabled. Expected revision is checked before upload and by the Documents API on append. Tenant/asset IDs from forms are not authorization; recovery assets are verified against the selected template. Existing Workspace create/logo routes remain compatible.

The plain form only projects losslessly supported header/footer/body/layout/watermark structures. Unsupported/rich parts remain readonly and are copied from the current server revision even if a caller forges plain input. Unchanged supported JSON is retained verbatim where possible. Header image append preserves existing nodes; replacing all managed top-level images is only available for the safe simple header structure. Assets and old revisions are immutable; there is no asset deletion in this slice.

New creation without an image writes revision 1. With an image, the Web orchestrates create, upload, then a combined content revision 2. Existing edits upload when necessary and append one combined revision. These are separate calls, not a distributed transaction. On known partial success, preserve the created template ID and any returned asset ID in an edit/recovery form. Never automatically retry unsafe requests. Ambiguous failures warn that a write may have happened and require checking the latest revision. Conflicts disable the stale form and link to a fresh server read in another tab. No exactly-once or unsaved draft recovery guarantee is introduced.

Image file selection validates PNG/JPEG and 5 MiB client/server metadata; Documents remains authoritative for actual signature/content validation and asset ownership. Object URLs provide local image-only previews, are revoked and never persisted. CSP adds `blob:` only to `img-src`; no external images or script relaxation. Template JSON and names are Razor-escaped. Full page preview, banner/alignment modes, footer policies, late pinning and Turkish UI belong to steps 3–6, not this change.

## Verification

Projection/controller tests cover preservation, new/edit orchestration, partial failure, scoped recovery, roles, subscription denial and stale version rejection. Local HTTP tests cover immutable old versions and pinned documents, image replacement/reuse, partial create recovery, rich content, CSRF and scope enforcement. Disposable Chromium tests exercise actual file preview, create, edit, selection/cancel, reload and desktop/mobile overflow. Browser/SQL tests are opt-in and the scripts allocate retained synthetic local fixtures. See the Persian guide in `docs/testing/template-editing.fa.md`.

Existing dependency warnings documented in ADR 0021 are not resolved or reclassified by this feature. No new packages were introduced.

Local verification on 2026-10-15: 467 ordinary .NET tests passed (465 domain + 2 architecture); 14 domain opt-in tests and the end-to-end placeholder skip in the ordinary run. The local template suite passed 67 editing checks plus 224 baseline/navigation checks and two Chromium tests. Existing purchase-order regression passed 664 HTTP checks and 11 opt-in SQL tests. JavaScript tests passed 35/35. Desktop/mobile screenshots were visually inspected, including long-name wrapping. Agent-owned local test hosts were stopped afterwards; synthetic SQL fixtures remain for inspection.
