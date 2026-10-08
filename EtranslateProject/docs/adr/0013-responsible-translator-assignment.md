# ADR 0013: Responsible translator assignment is coordination, not authorization

Status: Accepted for the development slice

## Context

Office teams now have separate translator and manager accounts. They need one responsible translator per job, a personal task list, and a trace of responsibility changes. These are separate concepts from who actually wrote a draft, who approved a PDF, and who may eventually sign a legal document.

## Decision

Workflow owns nullable `AssignedTranslatorUserId`, a monotonically increasing `AssignmentVersion`, the latest change actor/time, and append-only assignment history. Existing jobs migrate to unassigned/version zero without changing creation metadata. Assigning changes `UpdatedAtUtc` but not job status, signature policy, actual document authors, PDFs or review state. Assignment can therefore coordinate an internally approved job without reopening its document or altering the approved artifact.

Only active tenant Owner/Administrator accounts may assign, reassign or clear responsibility. Targets must currently be active Owner/Administrator/Translator members of the same tenant. Owner/Administrator can themselves do translation work, including an independent owner assigning themselves. Reviewer remains read-only and is ineligible. Identity exposes authenticated, manager-only candidate-list and individual eligibility APIs; Workflow forwards the manager's bearer authorization. The candidate list is limited to 200; individual target validation is not dependent on that list limit. No Workflow query reads Identity tables, and external user/tenant IDs have no cross-service database foreign keys.

Assignment is **not a security boundary**. All active members retain their existing tenant-scoped read/write/review permissions. `assignedToMe=true` is a server-side filter tied to the authenticated actor; it is applied before the existing skip/take paging. Clients cannot choose whose personal list they request. No automatic assignment occurs on job creation, draft save, review submission or invitation acceptance. Assigned responsibility does not establish authorship, translator credentials, electronic signature authority or notary acceptance.

Membership changes are independent transactions in Identity. Workflow checks eligibility at assignment time but cannot atomically commit across Identity and Workflow databases. A concurrent revocation may complete after authorization and leave an assignment pointing to a now-inactive/non-writing user. That assignment is preserved, not silently reassigned; subsequent tenant access checks still deny inactive users. Managers see an unavailable-candidate warning and can reassign or clear it. Restoring membership can make a retained assignment visible in My tasks again. Already-authorized requests may finish, consistent with ADR 0012. A stricter cross-service coordination protocol would be a separate design if future requirements demand it.

Every mutation requires the current assignment version. Stale and unchanged requests return 409 and create no audit/event. EF optimistic concurrency on the job and a unique `(TranslationJobId, Version)` history index serialize competing assignments. Domain change, history row and `TranslationJobAssignedV1` use the existing EF bus outbox transaction; events contain IDs, version and timestamp, not emails or the optional note. History includes prior/new assignee, actual manager ID, optional note (max 500 raw characters) and UTC time. Notes are visible to other active tenant members and should not contain secrets. No notification consumer or automated workflow transition is added.

The BFF retains its authentication, antiforgery, no-store and no-automatic-write-retry protections. A separate assignment page is linked from the editor with `target=_blank`/`noopener`, so ordinary assignment forms do not reload an unsaved translation editor. The page shows at most 50 history entries (older records remain stored). Managers resolve current eligible IDs to email labels; inactive/noneligible/historical members fall back to IDs. Nonmanagers see only IDs/history, not the manager-only member directory. The UI supports assign/reassign/clear without JavaScript. Lost or ambiguous HTTP responses require loading current state before retrying.

## Limits and future work

No deadlines, acceptance/decline of assigned work, workload balancing, notifications, multiple translators, designated reviewers, per-job confidentiality, seat billing, or electronic signatures are introduced. Before large-team production use, add candidate searching/pagination and audit pagination, richer member labels, resource/abuse limits and normal deployment hardening. Tenant membership remains the authoritative access boundary; do not present My tasks as access isolation.

## Verification

Domain/model tests cover role authority, null defaults, assign/reassign/clear, monotonic history, stale/unchanged requests, invalid IDs, bounded notes, preserved creator/signature/status metadata and EF concurrency/index configuration. `Verify-TranslationAssignments.ps1` runs the prior Web/team baselines, checks API and MVC permissions, active/same-tenant eligibility, antiforgery, actual actor recording, personal filtering, wrong-user impersonation attempts, concurrent mutation, deactivation/reactivation, and preserved internal reviews. It creates disposable development fixtures but leaves records available for inspection. Browser QA separately verifies the form and My tasks using two synthetic accounts.
