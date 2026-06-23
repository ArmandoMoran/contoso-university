# Contoso University → Azure: Implementation Plan

A sequenced, executable runbook that turns the recommendations in the migration dossier
(`docs/cloud-migration/architecture.drawio`) into a working Azure deployment.

Each phase ships something verifiable, leaves `master` releasable, and de-risks the next.
Treat every `[ ]` as a checklist item. Commands assume Git Bash / PowerShell on Windows with
the tools listed under **Prerequisites**.

---

## TL;DR — phase map

| Phase | Goal | Outcome | Touches |
|------:|------|---------|---------|
| **0** | Baseline & safety net | Pinned reproducible build, branch, green tests on 2.1 | repo, CI |
| **1** | Upgrade to .NET 8 (LTS) | Solution builds & all tests pass on `net8.0` | every `.csproj`, `Program.cs`, `Startup.cs` |
| **2** | Make it cloud-ready (code) | One SQL provider, real migrations, vaulted secrets, DP keys, health, telemetry | `ServiceCollectionExtensions`, `Startup`, `Data`, config |
| **3** | Containerize | One image per app, runs locally via Compose | new `Dockerfile`s, `docker-compose.yml`, `.dockerignore` |
| **4** | Provision Azure (IaC) | All resources exist, reproducible from Terraform | new `infra/` Terraform |
| **5** | CI/CD pipeline | Commit → image → migrate → staging → approval → prod | `azure-pipelines.yml` |
| **6** | Cutover & harden | DNS, private endpoints, autoscale, DR, cost review | Azure config |

**Estimated effort:** Phases 1–2 are the bulk (~3–5 dev-days incl. test fixes); 3–5 ~2–3 days; 6 ongoing.

---

## Decisions to confirm (defaults assumed below)

I've picked sensible defaults so the plan is concrete. Flag any you'd change before Phase 4.

| # | Decision | Assumed default | Alternative |
|--:|----------|-----------------|-------------|
| D1 | Compute | **App Service for Containers (Linux)** | Azure Container Apps / AKS |
| D2 | Environments | **`staging` + `prod`** (slots on one plan) | add `dev`; separate plans/RGs per env |
| D3 | Apps deployed | **Web + Api** as two App Services; **React SPA** as Azure Static Web App | combine; or host SPA from Web |
| D4 | SQL auth | **Entra Managed Identity** (passwordless) | SQL auth with vaulted password (faster to start) |
| D5 | Region / naming | `eastus`, prefix `contoso-univ`, RG `rg-contoso-univ-prod` | your standard |
| D6 | Edge | **Front Door + WAF** | Application Gateway; or none for MVP |
| D7 | IaC tool | **Terraform** (`azurerm` provider) | Bicep / ARM |
| D8 | CI host | **Azure DevOps Pipelines** (Workload Identity Federation, no stored creds) | GitHub Actions |

---

## Prerequisites (install once)

```bash
# Local toolchain
dotnet --list-sdks                      # need 8.0.x  (winget install Microsoft.DotNet.SDK.8)
docker --version                        # Docker Desktop
az version                              # Azure CLI  (az upgrade)
terraform version                       # Terraform >= 1.6  (winget install Hashicorp.Terraform)
az extension add --name azure-devops    # Azure DevOps CLI (provides az devops / az pipelines)
dotnet tool install --global dotnet-ef  # EF Core CLI (8.x)
dotnet tool install --global upgrade-assistant   # optional, helps Phase 1
```

```bash
# Azure context
az login
az account set --subscription "<SUBSCRIPTION_ID>"
az account show -o table
```

You will also need: an **Azure DevOps organization + project** and rights to create a service connection
(Workload Identity Federation auto-provisions an Entra app registration / federated credential — no stored secret),
and **Owner** or **Contributor + User Access Administrator** on the target subscription/RG (to assign roles).

---

## Phase 0 — Baseline & safety net

Goal: a known-good starting point you can always return to.

- [ ] Create the working branch: `git checkout -b feat/azure-migration`
- [ ] Confirm the 2.1 build is currently green locally (the Travis matrix): build + the three test projects.
- [ ] **Pin the toolchain** so CI/local agree. Replace the SDK pin in `global.json` once Phase 1 starts; for now record the current state.
- [ ] Capture a DB schema baseline for parity checks later (export current LocalDB schema or note seed expectations from `SeedData`/`ApiInitializer`).

**Acceptance:** branch created; you can build/test the existing 2.1 solution.
**Rollback:** none needed — no changes yet.

---

## Phase 1 — Upgrade to .NET 8 (LTS)  · dossier R1

> Do this first and in isolation. Nothing else is safe until the app builds and **all tests pass** on `net8.0`.

### 1.1 Retarget every project

- [ ] In all 10 `.csproj` files, set the framework:
  - Web SDK apps (`Web`, `Api`, `Spa.React`) and class libs (`Common`, `Data`) and all test projects:
    ```diff
    - <TargetFramework>netcoreapp2.1</TargetFramework>
    + <TargetFramework>net8.0</TargetFramework>
    ```
- [ ] Update `global.json`:
    ```diff
    - "version": "2.1.300"
    + "version": "8.0.400", "rollForward": "latestMinor"
    ```
- [ ] Remove the version pin on the shared framework where present and let the Web SDK imply it:
    ```diff
    - <PackageReference Include="Microsoft.AspNetCore.App" />
    ```
  For class libraries that use ASP.NET types (`Common` references `AspNetCore.Identity`, `Hosting`):
    ```xml
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    ```

### 1.2 Bump packages (2.1 → 8.0)

- [ ] `ContosoUniversity.Data.csproj`:
  - `Microsoft.EntityFrameworkCore*` `2.1` → `8.0.*` (`SqlServer`, `Design`)
  - **Drop** `Microsoft.EntityFrameworkCore.Sqlite` (the macOS branch goes away in Phase 2)
  - Keep `InMemory` only until Phase 2 swaps tests to Testcontainers
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore` → `8.0.*`
- [ ] `ContosoUniversity.Api.csproj`:
  - `Microsoft.AspNetCore.Authentication.JwtBearer` → `8.0.*`
  - `Swashbuckle.AspNetCore` `1.0.0` → `6.6.*` (API surface changed — see 1.4)
  - Remove `Microsoft.AspNetCore` / `Microsoft.AspNetCore.Mvc` / `StaticFiles` explicit refs (in the shared framework now)
- [ ] `ContosoUniversity.Spa.React.csproj`: `Microsoft.AspNetCore.SpaServices.Extensions` → `8.0.*` (SpaProxy model)
- [ ] Test projects: `Microsoft.AspNetCore.Mvc.Testing` → `8.0.*`, xUnit/Moq to current.
- [ ] Run `dotnet list package --outdated` to catch stragglers (AutoMapper, Newtonsoft, etc.).

### 1.3 Modernize the host (keep `Startup` to minimize churn)

`ContosoUniversity.Web/Program.cs` and `ContosoUniversity.Api/Program.cs` use 2.1 idioms. Move both
to the generic host while **retaining the `Startup` classes** (still supported) so the integration-test
`WebApplicationFactory` keeps working:

```csharp
// Program.cs (both Web and Api)
public static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(web => web.UseStartup<Startup>());
```

- [ ] Replace `WebHostBuilder`/`UseKestrel`/`UseIISIntegration` in `Api/Program.cs` with the above.
- [ ] Preserve the custom config (`ConfigConfiguration`) and logging wiring via `ConfigureAppConfiguration`/`ConfigureLogging`.

### 1.4 Fix breaking API changes (compiler will guide you)

- [ ] `IHostingEnvironment` → `IWebHostEnvironment` (in `Startup`, `ServiceCollectionExtensions`, initializers).
- [ ] Routing: in `Startup.Configure`
  ```diff
  - app.UseMvcWithDefaultRoute();
  + app.UseRouting();
  + app.UseAuthentication();
  + app.UseAuthorization();
  + app.UseEndpoints(e => e.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}"));
  ```
- [ ] `services.AddMvc()` → `AddControllersWithViews()` + `AddRazorPages()` (Web); `AddControllers()` (Api).
- [ ] Swashbuckle 6: `Swashbuckle.AspNetCore.Swagger.Info` → `Microsoft.OpenApi.Models.OpenApiInfo`; `services.AddSwaggerGen(c => c.SwaggerDoc("v1", new OpenApiInfo{...}))`.
- [ ] `AddJwtBearer` / Identity option names: verify against 8.0 (mostly source-compatible).
- [ ] AutoMapper `AddAutoMapper` signature/profile registration.

### 1.5 Green the build & tests

- [ ] `dotnet restore && dotnet build`
- [ ] Run the full test set (note: `ContosoUniversity.Web.IntegrationTests` exists on disk but is **not** in the `.sln` — add it or run explicitly):
  ```bash
  dotnet test ContosoUniversity.Data.Tests/ContosoUniversity.Data.Tests.csproj
  dotnet test ContosoUniversity.Web.Tests/ContosoUniversity.Web.Tests.csproj
  dotnet test ContosoUniversity.Api.Tests/ContosoUniversity.Api.Tests.csproj
  dotnet test ContosoUniversity.Web.IntegrationTests/ContosoUniversity.Web.IntegrationTests.csproj
  ```

**Acceptance:** solution builds on `net8.0`; all test projects pass; app runs locally against LocalDB.
**Rollback:** revert the branch; Phase 1 is self-contained.
**PR:** "Upgrade to .NET 8 (LTS)" — review independently before Phase 2.

---

## Phase 2 — Cloud-ready code  · dossier R2–R7

### 2.1 One database provider, from config  (R2)

- [ ] In `ContosoUniversity.Common/ServiceCollectionExtensions.AddCustomizedContext`, delete the
      `OperatingSystem.IsMacOs()` SQLite branch and the hardcoded LocalDB fallback. Keep the `Testing`
      in-memory branch only until 2.7.
  ```csharp
  options.UseSqlServer(
      configuration.GetConnectionString("DefaultConnection"),
      sql => sql.EnableRetryOnFailure());   // transient-fault resilience for Azure SQL
  ```
- [ ] Delete `ContosoUniversity.Common/OperatingSystem.cs` and `ContosoUniversity.Data/OperatingSystem.cs`.
- [ ] Move the connection string out of `appsettings.json` (it currently hardcodes LocalDB). Local dev → user-secrets / `appsettings.Development.json` (gitignored); cloud → injected (2.3).

### 2.2 Real EF migrations  (R5)

> The repo has only `*ModelSnapshot.cs` — **no migration classes**. Azure SQL needs a real migration history.

- [ ] After Phase 1 builds, regenerate a clean initial migration per context (run from the startup project that owns each context):
  ```bash
  dotnet ef migrations add InitialCreate --context ApplicationContext      -p ContosoUniversity.Data -s ContosoUniversity.Web
  dotnet ef migrations add InitialIdentity --context SecureApplicationContext -p ContosoUniversity.Data -s ContosoUniversity.Web
  ```
- [ ] Replace in-process schema creation: in non-Testing startup, stop relying on `Initialize()`/`EnsureCreated()`
      to build schema. Keep **seeding** gated to non-production only.
- [ ] Generate the deploy artifact CI will apply (idempotent, safe to re-run):
  ```bash
  dotnet ef migrations script --idempotent --context ApplicationContext      -o artifacts/migrate-app.sql      -p ContosoUniversity.Data -s ContosoUniversity.Web
  dotnet ef migrations script --idempotent --context SecureApplicationContext -o artifacts/migrate-identity.sql -p ContosoUniversity.Data -s ContosoUniversity.Web
  ```

### 2.3 Secrets → Key Vault, auth by Managed Identity  (R3)

- [ ] Add packages to Web/Api: `Azure.Identity`, `Azure.Extensions.AspNetCore.Configuration.Secrets`.
- [ ] In `Program.cs` config builder, layer Key Vault when a vault URI is present:
  ```csharp
  var vault = ctx.Configuration["KeyVaultUri"];
  if (!string.IsNullOrEmpty(vault))
      config.AddAzureKeyVault(new Uri(vault), new ManagedIdentityCredential());
  ```
- [ ] Migrate these out of config into Key Vault secrets: `Authentication:Tokens:Key` (**the symmetric JWT key — top risk**),
      `SendGridKey`, Twilio `SMSAccount*`, `Authentication:Google:*`, `Authentication:Facebook:*`.
- [ ] (D4) Passwordless SQL: connection string `Server=tcp:<sql>.database.windows.net;Database=<db>;Authentication=Active Directory Default;`
      — no password. The app's Managed Identity becomes a SQL user (see 4.4).

### 2.4 Persist Data Protection keys  (R4)

- [ ] Add `Azure.Extensions.AspNetCore.DataProtection.Blobs` + `.Keys`.
  ```csharp
  services.AddDataProtection()
      .PersistKeysToAzureBlobStorage(new Uri(blobUri), new ManagedIdentityCredential())
      .ProtectKeysWithAzureKeyVault(new Uri(keyId), new ManagedIdentityCredential());
  ```
- [ ] Without this, auth cookies break across instances / restarts — required before scaling past 1 instance.

### 2.5 Trust the edge & force HTTPS  (R6)

- [ ] Configure forwarded headers (Front Door / App Service sit in front):
  ```csharp
  app.UseForwardedHeaders(new ForwardedHeadersOptions {
      ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
  ```
- [ ] Remove the `EnableHttps` config toggle in `Startup.Configure`; make `UseHsts()` + `UseHttpsRedirection()` always-on outside Development.

### 2.6 Health checks + telemetry  (R7)

- [ ] Add `AspNetCore.HealthChecks.SqlServer` (or `AddDbContextCheck`) and endpoints:
  ```csharp
  services.AddHealthChecks().AddDbContextCheck<ApplicationContext>();
  app.MapHealthChecks("/health/live",  new(){ Predicate = _ => false });
  app.MapHealthChecks("/health/ready");
  ```
- [ ] Add `Microsoft.ApplicationInsights.AspNetCore` (or OpenTelemetry + Azure Monitor exporter): `services.AddApplicationInsightsTelemetry();`

### 2.7 Tests use real SQL  (R9)

- [ ] Replace EF `InMemory` integration tests with **Testcontainers** (`Testcontainers.MsSql`) so tests run the same provider as prod.
- [ ] Keep pure unit tests (Moq) as-is.

**Acceptance:** app runs locally against containerized SQL (next phase) or LocalDB with migrations applied;
no secrets in source; health endpoints respond; tests green.
**Rollback:** feature-flag Key Vault/DP via presence of env vars (already conditional) so the app still runs locally without Azure.

---

## Phase 3 — Containerize  · dossier R8

- [ ] Add a multi-stage `Dockerfile` for **Web** and **Api** (non-root, port 8080):
  ```dockerfile
  FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
  WORKDIR /src
  COPY . .
  RUN dotnet restore ContosoUniversity.Web/ContosoUniversity.Web.csproj
  RUN dotnet publish ContosoUniversity.Web/ContosoUniversity.Web.csproj -c Release -o /app

  FROM mcr.microsoft.com/dotnet/aspnet:8.0
  WORKDIR /app
  COPY --from=build /app .
  ENV ASPNETCORE_URLS=http://+:8080
  USER app
  EXPOSE 8080
  ENTRYPOINT ["dotnet", "ContosoUniversity.Web.dll"]
  ```
- [ ] Add `.dockerignore` (`bin/`, `obj/`, `node_modules/`, `**/appsettings.Development.json`, `.git`).
- [ ] Add `docker-compose.yml` for local dev: `web`, `api`, and `mcr.microsoft.com/mssql/server:2022-latest`.
- [ ] Modernize front-end (R9): replace **Bower** (`ContosoUniversity.Web/bower.json`) with LibMan or npm; pin Node for the SPA build.
- [ ] Verify: `docker compose up` → app reachable at `http://localhost:8080`, `/health/ready` returns healthy.

**Acceptance:** both images build and run; app works end-to-end against SQL in a container.

---

## Phase 4 — Provision Azure (Infrastructure as Code)

Create `infra/` with Terraform (`azurerm` provider) modules. Recommended resource set (D1–D7):

| Resource | Purpose | Notes |
|----------|---------|-------|
| Resource Group | container | `rg-contoso-univ-prod` |
| Log Analytics + Application Insights | observability | workspace-based |
| Azure Container Registry | images | `AcrPull` to app identities |
| App Service Plan (Linux) | compute | P1v3; one plan, two apps |
| Web App (container) + **staging slot** | MVC site | system-assigned MI |
| Web App (container) + **staging slot** | REST API | system-assigned MI |
| Azure SQL Server + Database | data | Entra admin set; PITR; failover group later |
| Key Vault | secrets + DP key | RBAC mode |
| Storage Account + blob container | DP key ring | `dp-keys` container |
| Azure Cache for Redis *(optional)* | cache/session | if needed |
| Static Web App | React SPA | or serve from Web |
| Front Door + WAF | edge | routes to Web/Api; OWASP ruleset |
| VNet + subnets + Private Endpoints | network isolation | can defer to Phase 6 |

- [ ] **Set up remote state first** (so CI and humans share one state, with locking). Create a backend storage account once:
  ```bash
  az group create -n rg-tfstate -l eastus
  az storage account create -n sttfstatecontoso -g rg-tfstate -l eastus --sku Standard_LRS
  az storage container create -n tfstate --account-name sttfstatecontoso
  ```
- [ ] Author Terraform (`infra/main.tf`, `variables.tf`, `providers.tf`, `*.tfvars`). Use the `azurerm` backend + provider:
  ```hcl
  terraform {
    required_version = ">= 1.6"
    required_providers { azurerm = { source = "hashicorp/azurerm", version = "~> 4.0" } }
    backend "azurerm" {
      resource_group_name  = "rg-tfstate"
      storage_account_name = "sttfstatecontoso"
      container_name       = "tfstate"
      key                  = "contoso-univ-prod.tfstate"
    }
  }
  provider "azurerm" { features {} }   # auth via OIDC / az login — never a stored secret
  ```
- [ ] Plan then apply (review the plan before applying):
  ```bash
  terraform -chdir=infra init
  terraform -chdir=infra plan  -var-file=prod.tfvars -out=tfplan
  terraform -chdir=infra apply tfplan
  ```
- [ ] Prefer Terraform resources over inline scripts for the role assignments and SQL setup in 4.4
      (`azurerm_role_assignment`, `azurerm_mssql_server_microsoft_support_auditing_policy` / AAD admin, etc.) so the whole environment is reproducible from state.

### 4.4 Wire up identity & access (passwordless)

- [ ] Grant each app's Managed Identity:
  - `Key Vault Secrets User` on the vault, `Key Vault Crypto User` on the DP key
  - `Storage Blob Data Contributor` on the `dp-keys` container
  - `AcrPull` on the registry
- [ ] Make the app a SQL user (run against Azure SQL as the Entra admin):
  ```sql
  CREATE USER [contoso-univ-web] FROM EXTERNAL PROVIDER;   -- the App Service MI name
  ALTER ROLE db_datareader ADD MEMBER [contoso-univ-web];
  ALTER ROLE db_datawriter ADD MEMBER [contoso-univ-web];
  ```
- [ ] Set app settings on each Web App: `KeyVaultUri`, `ConnectionStrings__DefaultConnection` (passwordless),
      blob/DP key URIs, `APPLICATIONINSIGHTS_CONNECTION_STRING`.

**Acceptance:** `terraform plan` shows no changes on re-run (idempotent); resources exist; app settings reference Key Vault.

---

## Phase 5 — CI/CD pipeline  · dossier §05

Replace `.travis.yml` with an Azure DevOps **multi-stage YAML pipeline** (`azure-pipelines.yml` at the repo root);
authenticate to Azure via an ARM **service connection using Workload Identity Federation** (no stored cloud secrets).

### 5.1 One-time Azure DevOps setup

- [ ] Create (or reuse) an Azure DevOps **organization + project** and connect this Git repo (Azure Repos, or a GitHub service connection if the repo stays on GitHub).
- [ ] Create an **Azure Resource Manager service connection** with **Workload Identity Federation**
      (Project settings → Service connections → New → Azure Resource Manager → *Workload Identity federation (automatic)*).
      This provisions an Entra app registration + federated credential — **no client secret is stored**. Name it e.g. `sc-contoso-univ-prod`.
  ```bash
  # CLI alternative (needs the azure-devops extension and a pre-created app registration):
  az devops service-endpoint azurerm create \
      --azure-rm-service-principal-id "<appId>" \
      --azure-rm-subscription-id "<sub>" --azure-rm-subscription-name "<subName>" \
      --azure-rm-tenant-id "<tenant>" --name "sc-contoso-univ-prod"
  ```
- [ ] Grant the service connection's identity **Contributor** on the target RG, plus the role assignments from 4.4 (`AcrPush`/`AcrPull`, SQL access for the migrate stage).
- [ ] Create pipeline **environments** `staging` and `prod` (Pipelines → Environments), and add an **Approval check** (and optional gates) on `prod`.
- [ ] Store non-secret config in a **variable group** (Library → Variable groups), e.g. `contoso-univ-prod`:
      `AZURE_SUBSCRIPTION_ID`, `ACR_NAME`, `RG_NAME`, `serviceConnection`. Link it to Key Vault for any secret the pipeline itself needs.

### 5.2 PR validation (`pr:` trigger + branch policy)

- [ ] Author `azure-pipelines.yml` with a `pr:` trigger on `master`; in the **Build/Test** stage:
      `dotnet restore/build`, run all test projects, build the React SPA (pinned Node via `NodeTool@0`).
- [ ] Security: enable **GitHub Advanced Security for Azure DevOps** (CodeQL code scanning + dependency scanning),
      run `dotnet list package --vulnerable`, and add the **Microsoft Security DevOps** task (`MicrosoftSecurityDevOps@1`).
- [ ] Build images with `Docker@2` (Buildx); scan with **Trivy**; **do not push** on PR.
- [ ] Add a **branch policy** on `master` (Repos → Branches → Branch policies) requiring this pipeline as **build validation**.

### 5.3 CD stages (push to `master`)

- [ ] Keep one multi-stage `azure-pipelines.yml`; gate CD stages with `condition: eq(variables['Build.SourceBranch'], 'refs/heads/master')`.
- [ ] **Build & push**: `AzureCLI@2` (via the service connection) → `az acr login` → build & push images to ACR tagged with `$(Build.SourceVersion)`.
- [ ] **Infra**: `terraform init` → `plan` → `apply` (Terraform extension tasks or `AzureCLI@2`); publish the plan as a pipeline artifact for review.
- [ ] **Apply migrations** against Azure SQL using the idempotent scripts from 2.2 (or `dotnet ef migrations bundle`),
      run from an `AzureCLI@2` step whose service-connection identity has SQL access.
- [ ] **Deploy to staging**: deploy the image to the **staging slot** (`AzureWebAppContainer@1` with `deployToSlotOrASE: true`, `slotName: staging`),
      targeting the `staging` environment; run smoke tests + poll `/health/ready`.
- [ ] **Gate:** the `prod` environment's **Approval check** blocks the swap until a reviewer approves.
- [ ] On approval: **slot swap** (`AzureAppServiceManage@0`, `action: 'Swap Slots'`) — zero-downtime. App Insights alert rule triggers auto swap-back on regression.

**Acceptance:** a commit to `master` flows commit → image → migrate → staging → approval → prod with no manual steps besides approval.

---

## Phase 6 — Cutover & hardening

- [ ] Custom domain + managed TLS on Front Door; point DNS; retire the old `*.adrianlimon.com` demo hosts.
- [ ] Lock down: Private Endpoints for SQL/Key Vault/Storage; restrict App Service to Front Door; enable WAF prevention mode.
- [ ] Autoscale rules (CPU/queue); SQL **failover group** to the paired region; verify PITR + geo-restore.
- [ ] Remove dead legacy: `.travis.yml`, `*.pubxml` publish profiles, `Release-Azure` config, `.bowerrc`/Bower.
- [ ] Cost review (right-size plan/SQL tier); set budgets + alerts.
- [ ] Update `README.md` with the new build/run/deploy story.

---

## Risk register

| Risk | Likelihood | Mitigation |
|------|:----------:|-----------|
| .NET 8 upgrade surfaces hidden behavior changes | Med | Phase 1 isolated + full test suite before anything else; Testcontainers (2.7) catches SQL-only bugs |
| Missing migrations → schema drift on Azure SQL | Med | Regenerate clean `InitialCreate` (2.2); apply idempotent scripts in CD; verify against schema baseline (Phase 0) |
| Auth breaks at scale (DP keys) | High if skipped | R4 before scaling past 1 instance; validate by swapping/recycling staging slot and re-checking login |
| Secret leakage during transition | Med | Move to Key Vault in 2.3; rotate the JWT key on cutover; scrub history if ever committed |
| Passwordless SQL misconfig blocks startup | Med | Keep SQL-auth-with-vaulted-password (D4 alt) as fallback; test in staging first |
| OIDC/role assignment gaps stall CD | Low | Validate 5.1 with a no-op deploy before wiring the full pipeline |

---

## Suggested PR sequence

1. **`feat/net8-upgrade`** — Phase 1 only (mergeable on its own).
2. **`feat/cloud-ready`** — Phase 2 (config, migrations, secrets, DP, health, telemetry).
3. **`feat/containerize`** — Phase 3 (Dockerfiles, compose, front-end cleanup).
4. **`feat/infra-terraform`** — Phase 4 (`infra/`).
5. **`feat/cicd`** — Phase 5 (`azure-pipelines.yml`), retire Travis.
6. Cutover & hardening tracked as Phase 6 issues.
