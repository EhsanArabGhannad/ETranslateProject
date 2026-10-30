# ADR 0046: Editor role, membership and subscription acceptance

Date: 2026-10-30

Status: Accepted for the isolated Chromium/SQL acceptance slice

## Decision

Preserve the current server permission model and exercise it through six actual services, fresh SQL databases and authenticated browser sessions. Owners, administrators and translators can manage documents; Reviewer is a read-only role. Owners/administrators can approve or return an internal review. Translator assignment is tracking/signing context, not an editing access restriction in this policy. An independent translator owns a separate workspace with the same owner write permissions.

No migration, content version, renderer version, payment integration or dependency changes are introduced. Production services still enforce membership, role, subscription, resource scope and document state per operation. The browser is not the authority for these decisions.

## Corrections found during acceptance

The editor view previously enabled create-document, initial-template and PDF-generation buttons based on subscription alone. Add a fail-closed role check for these controls and omit the template-change link for a read-only member. Body editing continues to require the existing document-review capability, supported content, write entitlement and current/unlocked revision.

When a save receives 402 or 403, retain its local JSON, keep the editor frozen and disable further saves, with Turkish feedback explaining that the unsaved text can be copied. Previously the generic failure handler re-enabled editing after these confirmed access denials. The dirty/unload warning remains active. There is no background clipboard access, new local persistence or automatic submission after a permission change. Transient transport/service errors retain the existing retry behavior; 409 keeps its existing conflict lock.

This is request-time enforcement: an already-open editor may accept local typing until an operation confirms changed access. No live permission polling/push is added. Reloading checks the current membership and subscription again.

## Acceptance host and checks

`Verify-EditorAccess.ps1` explicitly opts into `EditorAccessAcceptanceTests`. It reuses the guarded `IsolatedSqlProductHost`, with an allowlisted separate output category. It creates six new run-specific local databases, uses independent browser contexts and registered synthetic accounts, and kills only its own service processes before guarded database cleanup. No AppHost, pre-existing database, user account data, outgoing invitation delivery or payment provider is used.

Thirteen scenarios cover all four roles, independent/cross-tenant access, review capabilities/locks, live role change and member deactivation, valid/expired trial, PastDue/Canceled/Pending observations, a synthetic paid window, and anonymous requests. Real save/reload verifies the author and v10 descriptor. Historical views and paged navigation preserve content; forced clipboard events cannot mutate read-only views.

Rejected writes are sent both to the document API and the web save action using a valid antiforgery token. Image-upload and PDF-generation API writes are also checked. Document revisions, review state, image/source/PDF/review counts and stored blob paths/hashes must remain unchanged. Read-only/expired active members can still retrieve existing source, private image and archived PDF bytes; deactivated or foreign members cannot. A valid subscription in another workspace cannot unlock an expired workspace. A forged own-tenant path containing foreign job/document IDs returns 404.

Subscription states and the paid period are explicit fixtures only in the new Billing database. The paid fixture exercises entitlement recovery; it is not a payment callback or a claim that a gateway is integrated. See the [Persian test guide](../testing/editor-access.fa.md) for execution and evidence.

## Remaining coverage

This completes a role/subscription/workspace slice of roadmap step 7. A full browser/device matrix, simultaneous edits and assignment/template/review races across sessions, service outages and deployment validation remain separate. It does not establish general production readiness or change the editor's Word-import limitations.
