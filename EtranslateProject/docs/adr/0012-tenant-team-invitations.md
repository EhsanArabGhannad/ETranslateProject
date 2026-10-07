# ADR 0012: Tenant team membership and account-bound manual invitations

Status: Accepted for the development slice

## Context

Internal translation review needs a real translator account distinct from the office manager. Identity Access already owns tenant roles, and Workflow/Documents consult it for each tenant-scoped request. This slice adds membership management without coupling those services to Identity tables or treating a SaaS role as a legal translator credential.

## Decision

Identity owns `TenantMembership.IsActive`, invitations, a tenant `TeamVersion` concurrency token, and append-only team audit records. Existing memberships migrate as active. No account, membership or document history is physically deleted.

| Actor | May invite/manage | Protected targets |
|---|---|---|
| Owner | Administrator, Translator, Reviewer | Owner and self |
| Administrator | Translator, Reviewer | Owner, all Administrators and self |
| Translator / Reviewer | None | All |

Reviewer remains a read-only observer, consistent with existing service authorization. Owner/Administrator alone approve internal reviews. Template editing retains existing document-edit permissions. Every active member currently sees all tenant jobs; per-job assignment is not an isolation boundary yet.

Invitations target an already registered user ID, resolved from the account email. Registration currently does not verify ownership of that email. Therefore the manager must personally deliver the confidential link to the intended person through a trusted channel; email lookup alone is not identity verification. No automatic email is sent, and no qualification as a sworn translator is inferred.

Each invitation has a random 256-bit, 43-character URL-safe token, stored only as SHA-256. Acceptance requires an authenticated matching user ID and a fixed-time hash comparison. Expiry is seven days, with an exclusive expiry boundary. Preview is recipient-only and grants no membership. Acceptance is single-use; cancelled/expired links cannot be accepted. Pending expired invitations remain stored as Pending but are displayed as expired; cancel them before reissue. Lost creation responses require cancellation/reissue because the secret cannot be recovered.

Acceptance checks that the inviter is still an active manager allowed to assign the requested role. Loss of authority makes acceptance fail; it does not permanently cancel the invitation, and restoring authority can make an otherwise valid pending link usable again. Reactivation through a new invitation reuses the historical membership rather than adding a duplicate.

All team writes update the same tenant concurrency token, so concurrent role changes, cancellation, acceptance and membership changes cannot both commit against a stale authority snapshot. Acceptance anchors that version before rereading invitation and inviter authority. A filtered unique index also permits only one stored Pending invitation per tenant/recipient. Stale versions and database uniqueness/concurrency conflicts return 409. Audit rows and `TenantTeamChangedV1` are saved atomically with the mutation through the existing EF outbox. Events contain IDs/roles, never email addresses or invitation secrets. No notification consumer is added here.

The MVC BFF requires authentication and antiforgery for mutations. Its existing no-store response policy applies. The one-time issuance view contains the plaintext code intentionally; subsequent GET listings do not. The browser builds the link on the current origin, places the code in its fragment, prefills the acceptance form and removes the fragment from browser history. The secret is posted in the authenticated form body, not a query string, cookie, TempData or browser storage. Without JavaScript, copy the displayed code manually. Opening a link before sign-in may lose its fragment during the login redirect; reopen the same link after logging in.

## Consequences and limits

Revocation is checked through existing Identity access APIs on subsequent Workflow/Documents requests. Already-authorized requests may finish; this is not global bearer-token revocation or logout. Ownership transfer is deliberately unavailable. The tenant owner remains active and immutable.

This is a development implementation, not a production security review. Before production: verify account email/identity appropriately, add invitation issuance limits and abuse controls, paginate large member lists, localize/audit UI, implement secure delivery if needed, and apply existing HTTPS/key/storage deployment hardening. Invitations currently require manual delivery, and team size does not affect subscriptions or entitlements. Audit UI shows the latest 50 changes, invitations the latest 100; older database records are retained. No electronic signature, public document verification, notary approval or legal acceptance is added.

## Verification

Domain tests cover role ceilings, owner/self protection, soft rejoin, token binding/expiry/single-use and model concurrency/index constraints. `Verify-TenantTeam.ps1` creates isolated development fixtures and checks actual role separation across Identity, Workflow and Documents, HTTP antiforgery, wrong recipients/tokens, replay, cancellation, stale versions, concurrent issuance/acceptance, membership revocation/reactivation and private team isolation. It runs the existing Web baseline as well. Browser QA separately checks invitation creation, fragment prefill/removal, recipient acceptance and visible membership.
