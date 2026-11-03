# ADR 0051: Reuse paged layout for view changes and measure large documents locally

Date: 2026-11-04

Status: Accepted for the bounded local large-document performance slice

## Decision

Keep the measured unscaled page count until document content, image readiness or font readiness changes. Previously every editor state update, including a caret movement, could clone the entire document and run the row planner again. Zoom also repeated that work despite fixed paper dimensions. The paged controller now marks layout dirty on document changes, captured image load/error events and font loading completion. Entering/leaving paged mode resets it. It still follows the caret, updates navigation and applies the display scale on view changes.

The cache belongs only to the mounted view. It never enters JSON, history, requests, storage or the print pipeline. Validation and the existing fallback run on the next real measurement. A document growing past 50 pages returns to continuous view with its text intact; undo can restore a valid paged document. The server continues to compute its own row plan. Schema v10, renderer v20, migrations, dependencies, authorization and the PDF concurrency policy are unchanged.

## Measurement and acceptance

Four synthetic documents cover about 86,771 text characters, a 50-row/12-column table, ten small image occurrences plus four tables, and fifty manually started pages. The browser suite measures ready/paged activation time, twelve caret movements, four zoom changes, nineteen text insertions, font changes, undo/redo and PDF generation. A test-only MutationObserver counts full measurement clones; there is no production telemetry. Input samples measure from the captured input event to a second animation-frame callback, including editor work and frame scheduling. They are not hardware keystroke-to-display latency.

Paired pre-change and final runs use the same pinned Windows headless Chromium and byte-identical edited JSON. Each fixture previously measured twelve times during caret navigation and four times during zoom; the final run measures zero in both phases. Document edits and typography changes still trigger measurements. Timing figures vary between runs; no uniform typing-speed improvement is inferred.

Local acceptance guards are deliberately broad regression limits: ready/enable under 15 seconds and input p95 under 750ms. They do not define a production SLA. Actual observed timings are recorded in the [Persian guide](../testing/editor-performance.fa.md).

Two real-product documents additionally exercise login, managed image ownership, save/reload through the web backend, PDF generation and a repeated PDF request returning the same archived ID. They use six fresh SQL databases and real services. A third document exceeds the page limit and must preserve its content, fall back in the editor and return the existing HTTP 422 PDF rejection with no PDF record.

Across four synthetic and two real-product PDFs, the comparison independently derives marker names from source content and verifies every marker's first page, all page counts, letterheads, last-page-only footers, unsigned-draft labels and image draw counts. Repeated heading markers use their first occurrence. Twelve browser tests include ten existing regressions for text search, widths, merges, images, manual breaks, focus, fallback, saving and locks.

## Limits

This is one local Windows Chromium run, with one user at a time and small compressed image fixtures. It does not establish multiuser capacity, mobile/IME performance, worst-case image bytes/pixels, maximum schema node/JSON size, memory ceilings, process restart behavior or deployed network latency. The PDF renderer retains its one-slot concurrency and existing deadlines; no browser pooling or load balancing was introduced. Roadmap step 7 remains incomplete.
