# ADR 0048: Bounded editor requests and recovery from unknown outcomes

Date: 2026-10-31

Status: Accepted for the isolated network/service recovery slice

## Decision

Keep revision/round checks and the existing PDF uniqueness policy as the server authority. Do not automatically replay mutations after transport failure or infer that an aborted request did not commit. Preserve the mounted editor source and give Turkish feedback that the outcome is unconfirmed and the current revision/review state should be checked in a new tab.

`editor-request.js` owns a 45-second abort deadline for save, review and PDF requests. The deadline covers response consumption as well as waiting for headers, and is cleared on success/failure. The helper makes one fetch and adds no retry, queue, local persistence or vendor coupling. Existing image-upload timing remains separate. Network, abort and malformed-JSON failures in review/PDF flows use the same safe feedback instead of displaying parser/browser error details.

The save action now returns only its safe error message/status when the enhanced editor requests JSON. It does not attempt to reload the editor from the same unavailable dependency. The client already retains the submitted source. Successful saves retain their existing redirect flow, and non-JSON requests retain the existing view fallback. No content schema, renderer, migration, package, authorization rule or payment behavior changes.

After a recoverable failure the prior editable state is restored. A confirmed 409/402/403 still follows the existing conflict/access lock behavior. Successful navigation clears the dirty state; returning from the response-consumption callback must not mark a confirmed save dirty again. Server-side unsafe HTTP retries remain disabled in the web backend clients.

## Acceptance

`Verify-EditorRecovery.ps1` starts six real product services against six new run-specific SQL databases. The host can stop/restart only child processes it created; it keeps their names/project paths and appends restart logs. The suite does not enumerate or stop existing services, use AppHost, modify the user's network or connect to existing application databases.

Six real-product scenarios cover:

1. Offline before save: the independent browser context loses connectivity; persistent state is unchanged, and a manual retry after restoration creates exactly one new draft.
2. Save response lost after commit: a browser route forwards the real request, observes the committed response, then discards it. The local text remains; retrying the old expected revision returns 409 and cannot create revision 3.
3. Review response lost after commit: one round is recorded, local source is retained, and a duplicate submission is rejected rather than creating another round.
4. PDF response lost after commit: unsaved editor text is not persisted/printed; the saved revision produces one PDF, and retry returns the same PDF ID without a duplicate list entry/file.
5. Documents service outage: the owned service is stopped; a failed save preserves local text and persistent state. After restart, the manual retry saves once.
6. Billing service outage: mutations return `503 subscription_unavailable`, not expiry. Authorized document/asset reads remain available; a fresh editor is read-only until entitlement can be checked. After restart the original editor can retry successfully.

All scenarios check immutable revision 1, exact final source/version, managed image/source counts and archived PDF bytes. Additional browser coverage advances the browser clock to exercise the production 45-second save/review deadline without a real 45-second wait, verifies retry and retained source, and checks malformed review JSON. Node tests cover one-shot fetch behavior, abort before headers, the response-consumption deadline and timer cleanup. Controller tests cover bounded JSON failures.

See the [Persian guide](../testing/editor-recovery.fa.md) for results, execution and manual recovery.

## Limits

Preservation is in the current page's memory; there is no offline storage, automatic merge, background synchronization or reload/crash recovery. The abort signal does not prove server rollback. The six scenarios are bounded acceptance, not a full outage/chaos matrix: Identity, SQL Server, Web process restart, prolonged disconnects, other browsers/devices, upload ambiguity and production failover remain separate. Roadmap step 7 is still incomplete.
