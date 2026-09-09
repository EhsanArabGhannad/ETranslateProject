# ETranslate

ETranslate is a multi-tenant B2B SaaS platform for preparing, signing, verifying, and optionally notarizing translated documents in Türkiye.

## Architecture

The repository is a distributed monorepo. Each service is independently deployable and owns its data. Cross-service communication uses versioned APIs and integration events.

## Initial services

- Identity & Access: users, tenants, memberships, roles, and translator credentials.
- Translation Workflow: translation jobs and their lifecycle.
- Documents: templates, source files, rendering, immutable PDF versions, and hashes.
- Trust: electronic signatures, timestamps, validation, and public verification.
- Billing: plans, 30-day trials, subscriptions, entitlements, and usage.
- Notary Integration: physical, digital, and hybrid notary workflows behind an anti-corruption layer.
- Notifications: asynchronous email, SMS, and in-app notifications.

## Local development

The solution targets .NET 10 and uses Aspire for local orchestration and observability.

### Prerequisites

- .NET SDK 10.0.400 or newer in the 10.0 feature band.
- Docker Desktop or Podman for PostgreSQL and RabbitMQ containers.

### Run the platform

```powershell
dotnet tool restore
dotnet build ETranslate.slnx
dotnet run --project src/Orchestration/ETranslate.AppHost/ETranslate.AppHost.csproj
```

Open the Aspire dashboard URL printed in the terminal. The AppHost provisions two independently owned databases (`identitydb` and `billingdb`) and a RabbitMQ broker.

### Tenant onboarding flow

1. Register: `POST /api/v1/auth/register` on Identity Access.
2. Log in: `POST /api/v1/auth/login` and keep the returned bearer token.
3. Create a tenant: `POST /api/v1/tenants` with one of these string values:
   - `IndependentTranslator`
   - `TranslationOffice`
4. Billing consumes `TenantCreatedV1` and creates a 30-day trial automatically.
5. The internal subscription can be inspected at `GET /internal/v1/subscriptions/{tenantId}` on Billing. This endpoint is for service-to-service access and is not a public authorization boundary.

Example tenant request:

```json
{
  "name": "Example Translation Office",
  "slug": "example-office",
  "type": "TranslationOffice"
}
```

### Database migrations and tests

Both stateful services apply committed EF Core migrations at startup.

```powershell
dotnet test ETranslate.slnx
```

The end-to-end tenant/trial test requires Docker or Podman; domain and architecture tests run without containers.

### Translation jobs

Tenant members can manage draft translation jobs through:

- `POST /api/v1/tenants/{tenantId}/translation-jobs`
- `GET /api/v1/tenants/{tenantId}/translation-jobs`
- `GET /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}`
- `PUT /api/v1/tenants/{tenantId}/translation-jobs/{translationJobId}`

The bearer token is checked against Identity Access for every tenant-scoped operation. `AcceptanceProfile` may be omitted or null. When `NotaryRequirement` is `Required`, `NotaryProcessingMode` must be `Physical`, `Digital`, or `Hybrid`.

Example without a notary or special acceptance profile:

```json
{
  "title": "Tourist visa passport translation",
  "sourceLanguageCode": "fa",
  "targetLanguageCode": "tr",
  "notaryRequirement": "NotRequired",
  "notaryProcessingMode": null,
  "acceptanceProfile": null,
  "acceptanceProfileOther": null
}
```
