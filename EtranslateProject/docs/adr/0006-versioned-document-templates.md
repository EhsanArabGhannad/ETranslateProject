# ADR 0006: Tenant-scoped versioned document templates

- Status: Accepted
- Date: 2026-09-19

## Context

Independent translators and translation offices need reusable layouts for official translations, including page settings, letterheads, footers, and watermarks. A template can change over time, but a rendered or signed document must remain reproducible from the exact layout used when it was prepared.

## Decision

- Documents owns tenant-scoped `DocumentTemplate` aggregates and their revisions.
- Template names are unique within a tenant.
- Template revisions are immutable. Updating layout content creates the next numbered revision and requires `ExpectedCurrentRevision` for optimistic concurrency.
- A revision stores editor body content, optional header and footer content, page-layout settings, and an optional watermark definition as JSON.
- JSON is validated for syntax, root type, and size at the domain boundary. A later editor schema may add stricter semantic validation without changing revision identity.
- Templates are archived instead of deleted. Archived templates keep their history and cannot receive new revisions.
- Creation and revision events are published through the Documents transactional outbox.
- Binary branding assets such as logos are not embedded as base64 in template JSON. A dedicated immutable template-asset API will provide stable asset identifiers in the next slice.

## Consequences

- Future documents can reference a template and exact revision instead of a mutable current layout.
- Concurrent template editing cannot silently overwrite another user's changes.
- Tenant isolation is enforced both in authorization and every database query.
- Historical template content remains available after a template is archived.
- Page-layout and editor semantics remain frontend-independent for now, but schema evolution must be versioned before multiple editor formats are supported.
