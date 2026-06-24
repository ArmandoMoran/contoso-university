# CLAUDE.md

Guidance for working in this repository. Keep it current as the codebase changes.

## What this is

**Contoso University** — a demo ASP.NET Core solution that consolidates the official ASP.NET Core
tutorials into one university domain (students, courses, instructors, departments, enrollments).
It is a **well-factored monolith** split into a web app, a REST API, a React SPA, and shared layers.

## Solution layout

| Project | Role |
|---------|------|
| `ContosoUniversity.Web` | MVC + Razor Pages site; ASP.NET Core Identity (cookie auth), Google/Facebook OAuth |
| `ContosoUniversity.Api` | REST API + Swagger; JWT bearer auth |
| `ContosoUniversity.Spa.React` | React SPA + ASP.NET Core host (`ClientApp/`) |
| `ContosoUniversity.Common` | Shared services: Repository/UnitOfWork, DI extensions (`ServiceCollectionExtensions`), email/SMS, Identity setup |
| `ContosoUniversity.Data` | EF Core entities, DbContexts, migrations |
| `ContosoUniversity.Test` | Shared test helpers (`BaseIntegrationTest`, mocking) |
| `*.Tests` / `Web.IntegrationTests` | xUnit + Moq unit/integration tests; Selenium UI tests |

**Patterns:** Repository + Unit of Work, AutoMapper + DTOs, multiple `DbContext`s
(`ApplicationContext`, `SecureApplicationContext`/Identity, `ApiContext`, `WebContext`), options pattern.

## Build / test / run

```bash
dotnet restore
dotnet build

# Test projects in the CI matrix (.travis.yml):
dotnet test ContosoUniversity.Data.Tests/ContosoUniversity.Data.Tests.csproj
dotnet test ContosoUniversity.Web.Tests/ContosoUniversity.Web.Tests.csproj
dotnet test ContosoUniversity.Api.Tests/ContosoUniversity.Api.Tests.csproj

# Run locally
dotnet run --project ContosoUniversity.Web    # MVC site
dotnet run --project ContosoUniversity.Api     # API + Swagger UI
```

- **Single SQL Server provider** (`ServiceCollectionExtensions.AddCustomizedContext`) with
  `EnableRetryOnFailure`; EF **InMemory** only under the `Testing` environment (unit/controller tests).
  The connection string comes from config (user-secrets / `appsettings.Development.json` locally;
  injected in Azure) — no longer hardcoded, and the macOS/SQLite branch is gone.
- In **Development**, the schema/seed is created in-process via `dbInitializer.Initialize()`.
- Containers: `docker compose up --build` (web → :8080, api → :8081, SQL Server 2022).
- MVC client libs restore with **LibMan** (`libman restore`); the SPA builds via `npm` with Node
  pinned in `ClientApp/.nvmrc`. Secrets come from user-secrets locally, **Key Vault** in Azure.

## State / important findings

Phases 1–4 are **done** on `feat/azure-migration`: the solution builds on `net10.0` and the
unit/controller tests pass (Data 14 / Api 12 / Web 122). Key facts about the current code:

- **On .NET 10 (LTS).** All projects target `net10.0` (`global.json` SDK `10.0.100`). Class libraries
  use `FrameworkReference Microsoft.AspNetCore.App`; hosts use the generic host while **keeping the
  `Startup` classes** so the `WebApplicationFactory` tests keep working.
- **Real migrations exist.** `InitialCreate` (ApplicationContext) and `InitialIdentity`
  (SecureApplicationContext, under `Migrations/SecureApplication/`) are committed. The `*/Migrations/*`
  `.gitignore` rule that used to hide migration classes has been removed.
- **Secrets via Key Vault + Managed Identity.** JWT key, SendGrid, Twilio and OAuth secrets load from
  Key Vault at startup (config source in `Program.cs`, gated on `KeyVaultUri`); Azure SQL is reached
  passwordlessly (`Authentication=Active Directory Default`). No passwords in source.
- **Data Protection** keys persist to Azure Blob + a Key Vault key (gated on `DataProtection:BlobUri`);
  HSTS/HTTPS + forwarded headers are on outside Development; health checks at `/health/live` and
  `/health/ready`; App Insights when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set. All Azure wiring
  is config-gated, so local/test runs work without Azure.
- **`Web.IntegrationTests` is still not in the `.sln`** — run it explicitly. It now uses
  **Testcontainers** (real SQL Server), so it **requires a running Docker daemon**.
- **AutoMapper 15.1.1** (the DI-extensions package was merged into `AutoMapper` in v13). **react-scripts
  5** for the SPA — CRA is EOL, so a Vite migration is the eventual fix and transitive npm-audit
  advisories remain.

## Azure migration (in progress)

Target: **replatform** to Azure PaaS — .NET 10 containers on App Service, Azure SQL, Key Vault +
Managed Identity, Azure DevOps Pipelines CI/CD. Design maps 1:1 to AWS (table in the dossier).

**Full deliverables** (read these before doing migration work):
- `docs/cloud-migration/implementation-plan.md` — the executable, phase-by-phase runbook (commands, acceptance criteria, rollback, risk register).
- `docs/cloud-migration/architecture.drawio` — editable target architecture + CI/CD pipeline diagrams.
- `docs/cloud-migration/migration-dossier.html` — one-page write-up (AWS/Azure adaptation, code changes, architecture, build pipeline); open in a browser and Print → Save as PDF. Published: https://claude.ai/code/artifact/74d63914-edec-4127-83d8-987c837c71fc
- Migration dossier (the "why"): https://claude.ai/code/artifact/a202a9e0-ec35-41ab-9172-b2ffab0abdd6

### Phase map (see the runbook for detail)

| Phase | Status | Goal |
|------:|:------:|------|
| 0 | ✅ | Baseline & safety net — branch `feat/azure-migration` (2.1 is unbuildable on the current toolchain, so the upgrade *is* the baseline) |
| 1 | ✅ | **Upgrade to .NET 10 (LTS)** — projects retargeted, packages bumped, hosts modernized, breaking APIs fixed, tests green |
| 2 | ✅ | Cloud-ready code — single SQL provider, **real migrations**, secrets → Key Vault + Managed Identity, Data Protection key ring, forwarded headers/HSTS, health checks, telemetry |
| 3 | ✅ | Containerize — multi-stage Dockerfiles, Compose, Bower → LibMan (image build/`compose up` need a Docker daemon) |
| 4 | ✅ | Provision Azure with **Terraform** (`azurerm`, remote state) — `terraform validate` passes; `plan/apply` pending Azure creds |
| 5 | ⬜ | CI/CD — Azure DevOps Pipelines via Workload Identity Federation → ACR → migrate → staging slot → approval → blue-green swap |
| 6 | ⬜ | Cutover & harden — DNS/TLS, private endpoints, autoscale, DR, cost, cleanup |

### Confirmed decisions / defaults

- **IaC:** Terraform (`azurerm` provider), remote state with locking. *(was Bicep; changed per request)*
- **Compute:** App Service for Containers (Linux); Web + Api as two App Services; React SPA as Azure Static Web App.
- **Environments:** `staging` + `prod` via deployment slots.
- **SQL auth:** Entra Managed Identity (passwordless).
- **Edge:** Front Door + WAF. **CI:** Azure DevOps Pipelines with Workload Identity Federation (no stored cloud secrets).
- **Naming/region (placeholder):** `eastus`, prefix `contoso-univ`, RG `rg-contoso-univ-prod`.

### Suggested PR sequence

Phases 1–4 landed together on **`feat/azure-migration`** (one commit per phase). Remaining: `feat/cicd`
(Phase 5) → Phase 6 issues. The originally-planned per-phase split (`feat/net10-upgrade` →
`feat/cloud-ready` → `feat/containerize` → `feat/infra-terraform`) is described in the runbook.

## Conventions

- Keep `master` releasable; migration work is on `feat/azure-migration` (one commit per phase).
- Don't reintroduce secrets into source — use user-secrets locally, Key Vault in the cloud.
- When upgrading packages, prefer the implicit shared framework (`Microsoft.NET.Sdk.Web`) and
  `FrameworkReference` for class libraries over pinned `Microsoft.AspNetCore.*` package versions.
