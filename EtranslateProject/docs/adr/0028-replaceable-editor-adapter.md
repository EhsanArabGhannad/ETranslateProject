# ADR 0028: Replaceable editor boundary for the no-license-fee MVP

Status: Accepted for the client adapter seam; not approval of live pagination or a new content format

Date: 2026-10-23

## Context and decision

The user chose a no-license-fee first version and requested an easier future engine replacement. Keep the existing pinned Tiptap core as the MVP editing engine. Do not install a commercial SDK, activate an account, add the experimental pagination/table plugins, or switch to Canvas Editor. The free experiments retain their recorded RTL, overflow and print-parity failures; those results have not been fixed by this refactor.

Create a narrow application-owned boundary in `ClientApp/editors`:

- `document-contract.js` identifies the current supported rich-document vocabulary and validates JSON using the unchanged application allowlist. It is intentionally ProseMirror-shaped for compatibility, not unrestricted Tiptap JSON and not a universal, lossless document interchange format.
- `editor-session.js` exposes JSON export, allowlisted commands and command states, editable-state control, document/state subscriptions and disposal. It exports no native editor, selection, chain or vendor JSON. Providers implement the documented callback/handle contract.
- `tiptap-provider.js` owns all Tiptap/ProseMirror editing, selection, history and managed-image behavior for the body editor.
- `default-provider.js` makes one deliberate build-time engine selection. An untrusted query string, document field or tenant setting cannot choose a provider.
- `rich-editor.js` binds the application toolbar, localized labels, tenant-scoped asset resolver and fallback UI to a session. `workspace.js` coordinates saving and review through that session, not vendor methods.

The current capability descriptor explicitly reports no live pagination, DOCX import, faithful Word paste or merged cells. No new claim of fidelity follows from introducing an interface.

## Preservation and read-only behavior

The API still receives the existing unwrapped `editorContentJson` and `mode=rich` fields. There are no database migrations, server contract changes, renderer changes, archive rewrites or dependency changes in this slice. `etranslate.rich-document` version 1 is an **in-memory client contract descriptor**, not new metadata persisted on revisions or a completed backend version-dispatch mechanism.

Read the JSON before mounting. Reject unsupported structure/format/version without stripping fields; leave the original fallback/source available. Keep the pre-existing legacy empty-text normalization only in the editor's working copy. Loading, selection and temporary read-only changes do not emit document changes or create an initial undo entry. Export is checked against the same allowlist and size limit before submission.

A session mounted read-only cannot be upgraded to editable by a retry handler. Save/review temporarily freeze editing without emitting a synthetic content change. A failed save or review restores actual command eligibility, rather than enabling all toolbar buttons. A save conflict keeps the current unsaved body visible and locked. These client controls are a UX/data-preservation boundary, never a substitute for server-side authorization and concurrency checks.

The session owns provider subscriptions; the host owns DOM bindings. Disposal removes both, is idempotent and does not erase source JSON. Disposal is intended for an unmounted workspace; it does not reveal a stale legacy text field as an editable fallback.

## What replacement will still require

An alternative provider must translate its working model to/from the supported application vocabulary and pass preservation tests. An interface does not guarantee that a future vendor can represent every feature. Unsupported features must be rejected or reported by an explicit conversion workflow, not silently discarded.

Before introducing features outside the current vocabulary, add coordinated server-side format/schema-version dispatch, matching validation/rendering and explicit migration tests. Preserve the original revision; any conversion creates a new revision with provenance and a conversion report. Existing signed/archived PDF bytes and hashes must never be regenerated in place. Retaining only HTML or only a generic JSON envelope does not solve this problem.

Rendering remains an independent versioned concern. A new editing engine must pass font, direction, template-pin, last-page-footer and page/PDF parity checks before it can become an authoritative paginated view. Do not create a separate microservice per vendor just to expose these client methods.

Follow-up (2026-10-24): [ADR 0029](0029-versioned-document-content.md) adds persisted nullable content descriptors and version-aware server/client dispatch. This does not retroactively change the no-migration scope of the initial adapter slice or approve a richer schema/paginator.

## Verification

See the [test guide](../testing/editor-adapter.fa.md). Pure client tests exercise two fake providers with a different internal representation; they prove the seam, not compatibility with any uninstalled vendor. Opt-in browser tests load the actual production bundle and stylesheet under the production CSP from a temporary loopback-only test host, with synthetic responses and blocked external browser requests. A real local redirect exercises the save/reload path; the host is disposed after each test. They cover typing/history/table commands, payload preservation, save/reload, pending locks, retry, conflict, review failure, historical/read-only mounts, unsupported fallback, legacy loading and table limits. They do not exercise product APIs, SQL, sign a document or generate a PDF.
