# ADR 0055: Patch known vulnerable transitive NuGet packages

Date: 2026-11-04

Status: Implemented

## Decision

Override System.Security.Cryptography.Pkcs 9.0.11 with 9.0.21 in the five SQL-backed service projects and AppHost. The audit tool and DomainTests inherit that version through project references. SqlClient 6.1.6 and PDFsharp 6.2.4 introduced the older version. Override MessagePack 2.5.302 with 2.5.303 only in AppHost; its path is Aspire.Hosting.AppHost 13.5.3 → Aspire.Hosting/StreamJsonRpc 2.25.29 → MessagePack. Keep the existing major/minor package lines rather than upgrading the SQL/PDF/Aspire stacks for these fixes.

The patched versions are supported by the [Pkcs denial-of-service advisory](https://github.com/advisories/GHSA-2fmv-rv2c-59x7), [Pkcs signature-check advisory](https://github.com/advisories/GHSA-846f-f764-76mx) and [MessagePack 2.5.303 release](https://github.com/MessagePack-CSharp/MessagePack-CSharp/releases/tag/v2.5.303). The package warnings establish affected versions in the dependency graph; they do not prove that an attacker could reach each vulnerable API in this product.

Enable NuGetAudit explicitly with mode `all` in Directory.Build.props. Add an explicit `Verify-DependencySecurity.ps1` check that refreshes restore evaluation, audits direct/transitive dependencies, checks coverage against every solution project and verifies the resolved override versions in assets. Audit/source failures and NU1900–NU1905 warnings fail this check. It does not suppress warnings. An intentional future patch update should also update its expected versions. This is a runnable verification command, not an installed CI workflow or scheduled monitor.

## Validation and boundaries

NuGet reports zero known vulnerable packages across 17 projects at the time of this check. The solution builds with zero warnings/errors and ordinary tests pass. Real SQL-backed upload/PDF regression is run separately with fresh owned catalogs; see the [Persian report](../testing/dependency-security.fa.md).

This updates build inputs. It does not update the globally installed .NET SDK/runtime, redeploy running applications, exercise the Aspire dashboard/MessagePack RPC path at runtime, or audit JavaScript/OS dependencies. No AppHost is started against application databases. Future advisory changes can change the NuGet result; a clean audit is not a guarantee of absence of vulnerabilities. No renderer/schema/migration or document-content policy changes are introduced.

Follow-up: [ADR 0056](0056-isolated-document-to-signing-preparation-journeys.md) exercises office/independent document journeys through approval and unsigned preparation with fresh databases.
