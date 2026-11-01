# ADR 0049: Installed browser and emulated viewport acceptance

Date: 2026-11-02

Status: Accepted for the bounded Chrome/Edge and responsive editor acceptance slice

## Decision

Exercise the installed Chrome and Edge channels in independent headless profiles. Keep the pinned Chromium renderer and existing content contracts unchanged. Eight loopback cases cover desktop, 320px/390px portrait, landscape, tablet and a short desktop window. Five cases enable Playwright mobile layout/touch emulation and varied device scale factors. They still run the Windows browser binaries, with their actual user agents; they are not Android/iOS or physical-device tests.

The adapter fixture accepts an optional channel and context options. Existing tests keep their default bundled Chromium behavior; native Word capture keeps its existing headed Chrome behavior. The compatibility suite uses the production bundle, styles, tool partials and local fonts, but its surrounding HTML and response gates are synthetic. A separate suite starts six real services with six fresh run-specific SQL databases to exercise the rendered Razor page and real save/PDF/review paths in narrow Chrome, landscape Chrome and desktop Edge.

No product UI, content schema, renderer, migration, dependency, permissions or paid service changes were required by these cases. The scope is repeatable acceptance and an explicit browser/device support boundary, not a claim that all browsers or mobile workflows are supported.

## Acceptance

- Tool panels, controls and document remain within the outer viewport. Mobile checks also require `innerWidth` to equal the configured width so layout expansion cannot hide horizontal overflow. Horizontal scrolling of zoomed/paged paper is intentional.
- Touch-enabled cases use Playwright taps for buttons, disclosure panels and editor targets. Text entry uses automation input; it does not validate a physical keyboard, selection handles, IME, autocorrection or OS clipboard.
- The same synthetic document includes Turkish/Persian/ZWNJ text, a 47-row table, a private image, a manual last-page start, repeated letterhead and last-page-only footer. Page navigation and zoom retain source; text/cell edits and row changes survive undo/redo. A smaller viewport checks reachability without claiming OS-keyboard emulation.
- Synthetic save failure restores editing with retained source; retry/reload preserves content and resets history. Read-only sessions keep navigation available and editing locked.
- Real-product cases log in, display a private source image, open tool panels, edit the paged document, save and reload, generate/download the saved PDF, and submit review. SQL checks preserve the earlier draft, verify the new revision/PDF hash, and require a single review. Review freezes editing.
- The comparison script requires identical page counts and all 49 marker pages across the eight browser cases, three real-product editor documents and three PDFs. It also checks one body image, repeated headers, the last-page-only footer, saved edit and draft labels in every PDF.
- Page errors, CSP violations, external requests and owned-database cleanup are checked. Browser channels must already be installed; the runner does not download dependencies or attach to user profiles.

Run instructions and observed results: [Persian guide](../testing/editor-browser-compatibility.fa.md).

## Limits

Chrome and Edge are both Chromium-family browsers. This is one engine family, not independent Firefox/WebKit coverage. Safari/iOS, Firefox, native Android browsers, actual touch keyboards/IME/clipboard, pinch zoom, accessibility assistive technology, large-document performance and real network deployment remain separate. The SQL cases use a synthetic office owner with an active trial; the role/subscription matrix remains covered by ADR 0046, not repeated here. Roadmap step 7 is still incomplete.
