# ADR 0008: Server-rendered Web workspace as a Backend-for-Frontend

Date: 2026-10-04

Status: Accepted for the first development UI slice

## Context

The domain services already provide tenant-scoped job, document, template and immutable revision APIs. Web was an empty MVC scaffold and Gateway currently exposes only health endpoints. Browser-visible bearer tokens and direct database coupling are unnecessary for the initial UI.

## Decision

- Web uses MVC and service-discovered HTTP clients for Identity Access, Translation Workflow and Documents. AppHost wires these dependencies explicitly. Gateway routing is deferred; Web does not pretend the existing Gateway is a functioning proxy.
- Identity's opaque bearer token is stored inside an encrypted HttpOnly cookie ticket with access-token-aligned expiry, no sliding expiration and no retained refresh token. Every domain operation still enforces tenant access upstream; submitted tenant IDs are never proof of membership.
- Register/login/logout and workspace mutations use global MVC antiforgery validation. Output is Razor encoded. A same-origin Content Security Policy restricts scripts/images/forms. The asset proxy permits only upstream PNG/JPEG images and preserves upstream tenant authorization.
- Web's HTTP resilience disables retries for unsafe methods, without changing default behavior for existing API services. Ambiguous failed writes require a read/compare step instead of automatic replay.
- A first paragraph-based editor supports controlled plain-text-to-JSON conversion. Conversion is permitted only when the original immutable revision is a recognized plain-paragraph document, including a server-side check. Rich/unknown content stays JSON; preview limitations never transform the stored source. Managed images only use GUID-based asset routes, not arbitrary image URLs or HTML.
- Draft saves use expected revision numbers. HTTP 409 preserves the submitted body and requires manual merge. The enhanced browser save keeps the existing page intact on expired login, failed requests and conflicts. Historical revisions are shown read-only. Successful saves always append revisions, not overwrite.
- Templates can be created from simple header/footer/starter text and watermark. Logo upload then revision creation is a two-request operation, not an atomic transaction; failures can leave an unattached immutable asset. UI warns about this. Existing pinned documents stay unchanged.

## Consequences and boundaries

- No Web references to API assemblies or cross-service databases; replace routing later without changing document-domain behavior.
- Current interface labels are Persian for the development workflow. Localization, advanced rich editing, source-file UI, accurate print layout, PDF generation, signatures, QR verification, notary connectivity and billing checkout are separate slices.
- Preview rendering is approximate with a limited node vocabulary; page-layout settings are not a PDF renderer. No claim of legal validity follows from a preview or saved draft.
- Production requires HTTPS, persistent/shared Data Protection keys protected at rest, internal-only service endpoints, deployment/security review and shared blob storage. Cookie logout does not revoke other Identity bearer tokens.
- Session expiration requires a fresh login. No localStorage, autosave or persistent browser draft backup is used. Browser input preservation needs JavaScript and cannot survive closing the page.

## Verification

- Unit tests cover multilingual and HTML-like plain text round trips and refusal to flatten rich/unknown content.
- `Verify-WebWorkspace.ps1` exercises real cookie/antiforgery flows, onboarding, optional notary/profile and Other/Hybrid fields, template/logo creation, pinning, multilingual save, historical read-only presentation, stale-save input preservation and tenant isolation.
- Browser verification checks safe preview, successful image loading and actual editor submission. Unit tests and HTTP scripts complement rather than replace browser QA.
