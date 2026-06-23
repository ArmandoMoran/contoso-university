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

- Local DB: SQL Server **LocalDB** on Windows (connection string hardcoded in `appsettings.json`),
  SQLite on macOS, EF **InMemory** under the `Testing` environment — selected at runtime by OS in
  `ServiceCollectionExtensions.AddCustomizedContext`.
- In **Development**, the schema/seed is created in-process via `dbInitializer.Initialize()`.
- Secrets are read from user-secrets / config locally (SendGrid, Twilio, JWT key, OAuth).

## Gotchas / important findings

- **Framework is end-of-life.** Everything targets `netcoreapp2.1` (SDK pinned to `2.1.300` in
  `global.json`); .NET Core 2.1 has been unsupported since **Aug 2021**. The upgrade to .NET 10 LTS
  is the prerequisite for any cloud work.
- **Migrations are incomplete.** Only `*ModelSnapshot.cs` files are committed under
  `ContosoUniversity.Data/Migrations/` — **no migration classes**. A clean `InitialCreate` /
  `InitialIdentity` must be regenerated before any managed (Azure SQL) deployment will work.
- **Hosting model is inconsistent.** `Web/Program.cs` uses `WebHost.CreateDefaultBuilder` + `Startup`;
  `Api/Program.cs` hand-builds a raw `WebHostBuilder` (`UseKestrel`/`UseIISIntegration`). When
  upgrading, normalize both to the generic host **but keep the `Startup` classes** so the
  `WebApplicationFactory` integration tests keep working.
- **`Web.IntegrationTests` is not in the `.sln`** — run it explicitly; it won't build via the solution.
- **Data Protection uses the default local key ring** — Identity cookies/antiforgery break across
  multiple instances or container restarts. Must be persisted to shared storage before scaling out.
- **JWT signing key is a symmetric secret in config** — the sharpest security item; move to a vault and rotate.
- Front-end MVC libs use **Bower** (deprecated); the SPA builds via an MSBuild `npm run build` target.

## Azure migration (planned)

Target: **replatform** to Azure PaaS — .NET 10 containers on App Service, Azure SQL, Key Vault +
Managed Identity, Azure DevOps Pipelines CI/CD. Design maps 1:1 to AWS (table in the dossier).

**Full deliverables** (read these before doing migration work):
- `docs/cloud-migration/implementation-plan.md` — the executable, phase-by-phase runbook (commands, acceptance criteria, rollback, risk register).
- `docs/cloud-migration/architecture.drawio` — editable target architecture + CI/CD pipeline diagrams.
- Migration dossier (the "why"): https://claude.ai/code/artifact/a202a9e0-ec35-41ab-9172-b2ffab0abdd6

### Phase map (see the runbook for detail)

| Phase | Goal |
|------:|------|
| 0 | Baseline & safety net — branch, confirm green tests on 2.1 |
| 1 | **Upgrade to .NET 10 (LTS)** — retarget projects, bump packages, modernize hosts, fix breaking APIs, all tests green |
| 2 | Cloud-ready code — single SQL provider, **real migrations**, secrets → Key Vault + Managed Identity, Data Protection key ring, forwarded headers/HSTS, health checks, telemetry |
| 3 | Containerize — multi-stage Dockerfiles, Compose, drop Bower |
| 4 | Provision Azure with **Terraform** (`azurerm`, remote state in a storage-account backend) |
| 5 | CI/CD — Azure DevOps Pipelines via Workload Identity Federation → ACR → migrate → staging slot → approval → blue-green swap |
| 6 | Cutover & harden — DNS/TLS, private endpoints, autoscale, DR, cost, cleanup |

### Confirmed decisions / defaults

- **IaC:** Terraform (`azurerm` provider), remote state with locking. *(was Bicep; changed per request)*
- **Compute:** App Service for Containers (Linux); Web + Api as two App Services; React SPA as Azure Static Web App.
- **Environments:** `staging` + `prod` via deployment slots.
- **SQL auth:** Entra Managed Identity (passwordless).
- **Edge:** Front Door + WAF. **CI:** Azure DevOps Pipelines with Workload Identity Federation (no stored cloud secrets).
- **Naming/region (placeholder):** `eastus`, prefix `contoso-univ`, RG `rg-contoso-univ-prod`.

### Suggested PR sequence

`feat/net10-upgrade` → `feat/cloud-ready` → `feat/containerize` → `feat/infra-terraform` → `feat/cicd` → Phase 6 issues.

## Conventions

- Keep `master` releasable; do migration work on the per-phase feature branches above.
- Don't reintroduce secrets into source — use user-secrets locally, Key Vault in the cloud.
- When upgrading packages, prefer the implicit shared framework (`Microsoft.NET.Sdk.Web`) and
  `FrameworkReference` for class libraries over pinned `Microsoft.AspNetCore.*` package versions.
