# Infrastructure (Terraform · azurerm)

Provisions the Azure target: ACR, a Linux App Service Plan with the **Web** and
**Api** container apps (each with a `staging` slot and a system-assigned Managed
Identity), **Azure SQL** (Entra-only auth — no password), **Key Vault** (RBAC) +
a Data Protection key, a **Storage** account for the DP key ring, the React
**Static Web App**, **Front Door + WAF**, Log Analytics + Application Insights,
and the RBAC role assignments that make all access passwordless.

## Prerequisites

- Terraform `>= 1.5`, Azure CLI (`az login`)
- Rights to create resources and **assign roles** (Owner, or Contributor + User
  Access Administrator) on the subscription.

## One-time: remote state backend

```bash
az group create -n rg-tfstate -l eastus
az storage account create -n sttfstatecontoso -g rg-tfstate -l eastus --sku Standard_LRS
az storage container create -n tfstate --account-name sttfstatecontoso
```

Adjust the names to match the `backend "azurerm"` block in `providers.tf`.

## Deploy

```bash
cp prod.tfvars.example prod.tfvars   # fill in subscription/tenant/admin IDs
terraform init
terraform plan  -var-file=prod.tfvars -out=tfplan
terraform apply tfplan
```

Re-running `plan` should show no changes (idempotent).

## After apply: make each app a SQL user (passwordless)

Terraform sets the Entra admin on the SQL server but cannot create contained
database users. Connect to the database as the Entra admin and run, using the
`*_app_principal_id` outputs (the App Service Managed Identity names resolve by
display name):

```sql
CREATE USER [app-contoso-univ-web-prod] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [app-contoso-univ-web-prod];
ALTER ROLE db_datawriter ADD MEMBER [app-contoso-univ-web-prod];
-- repeat for the api app and, if they deploy independently, the staging slots
```

## Notes

- `validate` offline: `terraform init -backend=false && terraform validate`.
- VNet + Private Endpoints are deferred to Phase 6 (the SQL firewall currently
  allows Azure services).
