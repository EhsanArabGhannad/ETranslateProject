# ADR 0026: Turkish interface, independent document language

Status: Accepted for the development slice

Date: 2026-10-19

## Decision

Step 6 makes the single product interface Turkish: root `lang="tr"`, `dir="ltr"`, and `CurrentUICulture=tr-TR`. There is no language switcher. Browser headers, query strings and culture cookies do not change the interface locale. Request data culture remains invariant to preserve existing form/API contracts; UTC accounting, Istanbul display timestamps, trial duration, calendar-month arithmetic and persisted JSON are unchanged. Display-only TRY prices use Turkish grouping/decimal separators.

Product copy is held in `src/Web/ETranslate.Web/Resources/interface.tr.json`. It is embedded as a neutral resource (`WithCulture=false` is essential for the `.tr.json` filename) and used by `UiText` in Razor/BFF, DataAnnotation resource accessors and bundled clients. Client substitutions use textContent; Razor encodes resources and user arguments normally. The plain site script receives its confirmation/validation copy as encoded form data attributes. Changing copy requires rebuilding the checked-in client bundles. This catalogue is product copy only, not user templates or translated documents.

HTML number controls submit decimal points independently of the browser's visible language. The four template margin fields use a narrow invariant decimal binder which rejects comma grouping: `2,5` must not silently become 25 mm. Existing layout bounds remain authoritative. Form values/error recovery, revision concurrency, read-only history, subscription, membership, antiforgery and content-preservation policies remain unchanged.

Document JSON direction attributes (`auto`, `rtl`, `ltr`) and physical left/center/right alignment retain their semantics. User-written fields and content regions use independent auto direction. No migration, document rewrite, template revision rewrite, PDF cache invalidation or renderer version bump is introduced. Persisted statements and plan/order snapshots are not translated. Only default **future** catalogue plan names are changed in Billing configuration; codes, prices and snapshots are unchanged.

## Verification and boundaries

Tests cover resource lookup/validation, culture separation, safe formatting and strict decimal binding. `Verify-TurkishInterface.ps1` uses isolated local fixtures and a disposable Chromium with Persian browser locale to check the Turkish menu, multiple routes, multilingual template/content round trips, decimal persistence, history immutability and desktop/390/320px layouts. Existing UI assertions are updated to Turkish while Persian/Arabic document fixtures remain untouched. See [Persian test guide](../testing/turkish-interface.fa.md).

Step 7 remains the comprehensive release regression/documentation pass. Real signing, notary integration, payment gateway, legal acceptance and the dependency-security follow-up in ADR 0021 are not delivered by this localization step. Native browser file-picker/validation language is controlled by the user's browser/OS, not this catalogue.

During local QA, one initial host start hit the existing concurrent MassTransit SQL-infrastructure DDL race (`PublishMessageV2`, SQL 2021). Restarting only the agent-owned test host restored service health. No transport or database changes were made for this unrelated startup issue; it remains a separate hardening concern before release.
