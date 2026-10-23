# ADR 0030: Continuous paper view and engine-neutral focus mode

Date: 2026-10-24

## Scope and decision

Implement the view-only portion of editor roadmap step 3 on the existing Tiptap adapter. No new dependency or license is needed. This is **not live pagination**: the white editing surface has a minimum sheet height and grows continuously with the body. It has no automatic page splitting, page count, page navigation or manual page breaks. The saved revision's PDF remains authoritative for page boundaries.

`editor-view-layout.js` validates the pinned template's existing `DocumentPageLayout` vocabulary (A4/A5/Letter/Legal, portrait/landscape, margins, known footer placement). An absent template uses the same legacy A4 defaults. Unknown geometry leaves the original fluid editor available with a Turkish warning; it does not guess a layout or reinterpret document metadata. Unsupported content versions never mount this view, just as they never mount the rich editor.

`editor-view.js` has no vendor or content dependency. Fixed unscaled paper geometry uses CSS pixels at 96/inch. Fit-to-width (maximum 100%) and 75/100/125/150% use a transform and a matching layout-space wrapper; they do not change text size, line wrapping, content JSON, format/schema descriptors, history, saved preferences, requests or renderer configuration. Resize observation updates the scaled wrapper after edits or container changes. Overflow stays inside the keyboard-accessible paper viewport; long bodies remain reachable by scrolling.

Stable scrollbar gutters avoid a fit/scrollbar width feedback cycle near the viewport's maximum height.

The paper contains only the translation body, not editable header/footer/watermark copies. Template presentation remains in the existing approximate preview and saved PDF. This view does not establish visual/PDF equivalence or promise that a minimum sheet height corresponds to one PDF page.

## Focus mode and accessibility

Focus mode fills the **application window**, not the OS/browser fullscreen API. The existing editor card becomes a fixed dialog without reparenting or remounting the editor. Its save, revision and review controls remain available by scrolling. A sticky Turkish toggle and Esc exit the mode. Native selection and undo history remain on the same editor DOM. View controls are outside the draft form and outside the formatting-command toolbar.

While expanded, sibling branches up to the body are inert and keyboard traversal wraps inside the card. Prior inert state, dialog attributes, scroll position and focus are restored on exit. Page hide releases the dialog state (including for a cached navigation); explicit teardown also removes listeners and resize observation. Esc is handled even if an editor prevents its native default without stopping propagation; an inner popup may stop propagation when consuming Esc. IME composition is not intercepted.

View changes never call `setEditable` or issue content commands. Pending-save, conflict, historical, role/subscription and review locks continue to be enforced by the existing host/session/server paths. Viewing is not authorization to edit.

## Verification and boundaries

See the [Persian test guide](../testing/editor-paper-view.fa.md). Tests cover geometry, allowed zoom, no vendor/content coupling, unchanged JSON and dirty state, fixed text metrics, selection, same DOM identity, undo/redo, mixed-direction text, tables, keyboard focus containment/return, prior inert restoration, save/conflict locks, read-only view controls, mobile overflow and a long synthetic body. Screenshots use the production CSS and bundle in the existing synthetic loopback browser fixture under production CSP, not a personal browser or customer document.

No API, schema, migration, service startup, stored PDF or print rendering changes. No SQL connection, real save, PDF creation or external service is used in this step. Product-backed acceptance testing and PDF/page equivalence remain separate work. The earlier intermittent browser-test failure recorded with ADR 0029 remains an observation, not a defect claimed fixed by this change.
