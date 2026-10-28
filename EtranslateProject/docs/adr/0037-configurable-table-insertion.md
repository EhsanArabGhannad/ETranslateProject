# ADR 0037: Configurable table insertion through the application editor contract

Date: 2026-10-28

Status: Accepted for this development slice

## Decision

Expose the existing rectangular table vocabulary with a size picker instead of only a fixed 3x3 command. New table size is 1..50 rows and 1..12 columns, with an explicit boolean heading-row choice. The heading counts toward the total rows. The initial default remains 3x3 with headings. Settings apply to the next inserted table; they do not resize a selected table or change a stored heading. Existing row/column tools remain available inside tables.

The collapsed Turkish panel offers an 8-column by 6-row quick grid and numeric text fields for the full supported range. Hover/focus previews the grid rectangle; click or Enter selects its size, and the separate `Tablo ekle` command inserts it at the editor selection. Arrow/Home/End navigation uses one tab stop and bounded coordinates. Selecting options, opening the panel and hovering do not dirty the document, change its descriptor or add undo entries. Invalid input is reported and disables insertion without clamping or preventing an unrelated draft save. Text fields have no name or native form validation, and Enter in them cannot submit the draft.

The engine-neutral `insertTable` input is exactly `{rows, columns, withHeaderRow}`. Omitted input retains the old 3x3 default. Unknown fields, strings, fractions, out-of-range values and nonboolean heading choices are rejected in both the session and provider. No vendor `cols`, HTML, native document or selection leaks into the host. Values are copied before provider dispatch. A replacement provider implements this same application input.

Insertion requires an editable top-level paragraph/heading, outside tables/lists/quotes. Existing session locks and workspace state apply to every action, including forced events. Size controls freeze on ineligible selections, save/review requests and readonly/history mounts; failed requests recompute eligibility. The provider retains the transaction preservation filter and row/column limits, including keyboard-generated rows. Insertion closes history before and after the transaction, so typing inside the new table can be undone separately from creating it.

No new saved nodes, attributes, fonts, packages, renderer behavior or database columns are introduced. Rich content remains schema 5 and the renderer remains v15; legacy content is not rewritten by mounting or choosing a size. Explicit rich save follows the existing schema-5 policy. Existing PDFs, hashes, reviews, tenant access, subscriptions and signing preparations retain their existing behavior. General Word paste/import, merged cells, unequal widths, styling, nested tables and inline cell images remain outside this slice.

## Verification

Node tests exercise typed command limits and dispatch across a replacement provider, including defaults, locks and malformed inputs. The production-bundle browser harness uses the real table partial and verifies grid mouse/keyboard interaction, dimensions, heading choices, no dirty state before insertion, invalid sizes, minimum/maximum tables, blocked keyboard row growth, isolated Undo/Redo, save/retry/reload, readonly forced events and a narrow-screen layout.

A second scenario types synthetic Persian/Turkish content into two UI-created tables, saves/reloads them, then compares the actual editor marker pages with the existing PDF renderer. The PDF verifier checks all 68 markers, heading repetition across table pages and a nonheading first row that appears once. PDFs and editor screenshots are visually inspected. These tests use a temporary loopback host without starting AppHost or connecting to the existing SQL databases. Wider deployment, role/subscription/concurrency acceptance and other browser engines remain separate work.
