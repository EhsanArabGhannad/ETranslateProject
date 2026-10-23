# ADR 0027: Research boundary for a paginated document editor

Status: Proposed; baseline and two free-engine experiments complete, no replacement engine approved

Date: 2026-10-20

## Decision for this slice

Do not put a homegrown paginator or an untested commercial SDK in the production workspace. Keep production JSON contracts, authorization, immutable draft/template pins and archived PDF rendering unchanged. The user's permission to consider paid components is not authorization to purchase, register an account or activate an external service.

Add a local-only experiment under `experiments/editor-engine-step1`, using the already installed pinned Tiptap packages and existing renderer fonts. An allowlisted loopback server serves synthetic fixtures only. No production endpoint, storage, import/upload handler, clipboard importer or document migration is introduced.

The preview is explicitly read-only whole-block measurement, not paginated editing. It terminates on oversized blocks rather than clipping content or looping. The synthetic HTML experiment measures baseline formatting gaps; it is not evidence of fidelity to an actual Word clipboard or DOCX. Test reports must distinguish locally measured baseline results from vendor documentation and untested commercial candidates.

## Proposed direction, not a purchase decision

Prefer a ready document-layout engine over building a Word processor's pagination from scratch. Evaluate Syncfusion DOCX Editor first for the requested Word-oriented workflow and ASP.NET Core integration; retain Tiptap Pro Pages as the second candidate and ONLYOFFICE as an operationally larger alternative. This prioritization is engineering judgment, not a passed SDK benchmark. See the [research report and primary sources](../research/editor-engine-step1.fa.md).

Before accepting an engine, prove Persian/Turkish editing and clipboard behavior, table overflow handling, page/PDF agreement, template locks and last-page footers, local processing boundaries, .NET/Windows compatibility, and SaaS license fit. Do not use demo endpoints for real documents. Obtain trial access through the user's authorized process.

Any eventual content-format change requires an explicit versioned adapter/renderer design and preservation of legacy history. No existing revision or archived PDF is converted in place. Schema/renderer work remains a later step after the SDK evaluation gate, not an implicit outcome of this research slice.

## Follow-up: free pagination evaluation

The user subsequently requested and approved a free-first experiment. `experiments/tiptap-pagination-free` isolates Pagination Plus 3.1.0 and Table Plus 3.1.0 with the unchanged production Tiptap 3.31.4 peers. It is editable but not production-approved: text keyboard edits and history work, PDF marker page assignments differ, companion tables violate the current schema, and an oversized cell caused runaway page counts. A lab watchdog disables pagination without deleting the source; it does not fix the engine. See the [measured findings](../research/free-pagination-trial.fa.md). Evaluate Canvas Editor independently next, or explicitly scope repairs; do not relax production validation or migrate history to accommodate this unapproved prototype.

## Follow-up: Canvas Editor 1.0.5

The independently installed npm package was tested with synthetic fixtures in `experiments/canvas-editor-trial`. Pagination, keyboard input, native history and tall-cell rendering worked for these samples, but Persian logical cursor positions advanced left-to-right and mixed-script layout differed from a browser reference. Image-based export produced four raster pages with no extractable text; host-rendered native HTML produced three pages and differing marker assignments. Neither path is production rendering or an approved replacement. See [measured Canvas findings](../research/canvas-editor-trial.fa.md). A controlled commercial trial is the proposed next evaluation, not a purchase; alternatively scope repairs to the free Tiptap approach explicitly. No automatic schema migration, archived-PDF rewrite or untested SDK integration is authorized by these findings.

## Follow-up: no-license-fee MVP boundary (2026-10-23)

The user subsequently selected the free-first direction and approved the next step of making the current engine replaceable. [ADR 0028](0028-replaceable-editor-adapter.md) records the production client adapter seam around existing Tiptap core. This supersedes the research-only boundary only for that explicitly scoped refactor; it does not approve either experimental paginator, any commercial activation, a new persistence schema or an archived-PDF conversion.
