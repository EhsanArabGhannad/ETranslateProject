# ADR 0021: Tenant-scoped navigation and separate template settings

Status: Accepted for the development slice

Date: 2026-10-15

## Decision

This is step 1 of template-management improvements: separate navigation/pages only, not template editing, new image layouts, footer policies or late template attachment.

Authenticated Web pages share a responsive menu: jobs, My tasks, team (managers only), subscription, settings. Scope comes from the protected page's server-loaded model, never an arbitrary query value/cookie/browser storage. Links and the brand preserve that scope. Unknown/empty contexts omit scoped manager/subscription links; unaccepted invitations must not select the inviting tenant. Menu visibility is not authorization. Existing service membership/role checks, antiforgery and subscription filters remain authoritative.

`AppNavigation.From` explicitly covers workspace/editor, assignment, signing, team, subscription/orders and settings models. New protected page models must be added to this mapping to retain their selected scope. The editor additionally reads scoped Identity tenant metadata after its original job authorization/read, preserving existing 403/404 ordering and validating the returned tenant ID. No cross-service table access or shared tenant cache is introduced.

`SettingsController.Index` reads only live Identity memberships; it does not require Documents/Billing availability. `/Settings/Templates` selects the requested membership (404 for unknown explicit scope, 400 for malformed/empty UUID), observes the existing subscription policy and reads the scoped template list. With no memberships it shows a create-workspace link without provisioning anything. Expired/pending/unavailable subscriptions keep lists readable with disabled mutation controls; unauthorized/forbidden lease checks fail closed. Reviewer reads but cannot edit. Owner/Administrator/Translator retain existing template-write roles.

The original template/create/logo forms move from Workspace to `/Settings/Templates`. Existing protected POST `/Workspace/CreateTemplate` and `/Workspace/AddLogo` remain, with the same payloads, size limits, upstream operations and CSRF policy. Their success/validation redirects now return to scoped Settings/Templates. This avoids a business-API migration or legacy-link break. Logo upload remains a separate step and still appends an image in a new immutable template revision; a unified create/edit/logo experience belongs to step 2.

Workspace no longer reads or displays template management data/forms. Editor retains its template selector and pinning behavior, and links to template settings in a separate tab so navigation does not discard unsaved translation text. Pin-before-first-save, revision/PDF history, existing drafts and service ownership remain unchanged. No database migration, payment capability or signature behavior changes.

## Verification

Controller/navigation unit tests cover scope, current roles, no-membership state, invalid scope, readonly subscription states and loss of authorization. Updated Web baseline creates/uploads templates through the dedicated page while exercising unchanged mutation routes. Local navigation suite verifies routing, redirects, CSRF, cross-tenant isolation, Reviewer denial and includes a disposable headless Chromium test for scope-preserving menu links, My tasks, template forms and desktop/mobile overflow/screenshots. Existing signing/subscription/order regressions remain in the live suite. Fixtures are synthetic, retained and loopback-only; never run against production.

The normal checkout was running under the user's Visual Studio debugger during this step. It was not stopped. Isolated `--artifacts-path tmp/navigation-build` output and a separate Web process on port 5068 were used against the existing local development APIs. Only the agent-owned Web process is stopped after verification. The user must stop/rebuild/restart their normal run to load the new compiled pages.

## Separate dependency warning

An isolated fresh restore surfaced existing transitive `System.Security.Cryptography.Pkcs` 9.0.11 NU1902/NU1903 audit warnings. This menu change does not upgrade packages or assess exploit reachability. Before production, track runtime/dependency remediation separately using the [Microsoft advisory for CVE-2026-84008](https://github.com/advisories/GHSA-846f-f764-76mx) and [CVE-2026-70162](https://github.com/advisories/GHSA-2fmv-rv2c-59x7). A successful UI test is not a production-security clearance.
