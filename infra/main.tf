locals {
  name = "${var.prefix}-${var.environment}"
  # Storage/ACR names must be globally unique, lowercase, alphanumeric only.
  compact = lower(replace("${var.prefix}${var.environment}", "-", ""))

  tags = merge({
    application = "contoso-university"
    environment = var.environment
    managed_by  = "terraform"
  }, var.tags)
}

resource "azurerm_resource_group" "main" {
  name     = "rg-${local.name}"
  location = var.location
  tags     = local.tags
}

# ---------------------------------------------------------------------------
# Observability
# ---------------------------------------------------------------------------
resource "azurerm_log_analytics_workspace" "main" {
  name                = "log-${local.name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.tags
}

resource "azurerm_application_insights" "main" {
  name                = "appi-${local.name}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  workspace_id        = azurerm_log_analytics_workspace.main.id
  application_type    = "web"
  tags                = local.tags
}

# ---------------------------------------------------------------------------
# Container registry
# ---------------------------------------------------------------------------
resource "azurerm_container_registry" "main" {
  name                = "acr${local.compact}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Standard"
  admin_enabled       = false
  tags                = local.tags
}

# ---------------------------------------------------------------------------
# Compute: one Linux plan, two container apps (Web + Api), each with a slot
# ---------------------------------------------------------------------------
resource "azurerm_service_plan" "main" {
  name                = "asp-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  os_type             = "Linux"
  sku_name            = var.sku_app_service_plan
  tags                = local.tags
}

locals {
  common_app_settings = {
    "ASPNETCORE_ENVIRONMENT"                = "Production"
    "WEBSITES_PORT"                         = "8080"
    "KeyVaultUri"                           = azurerm_key_vault.main.vault_uri
    "APPLICATIONINSIGHTS_CONNECTION_STRING" = azurerm_application_insights.main.connection_string
    "DataProtection__BlobUri"               = "${azurerm_storage_account.main.primary_blob_endpoint}${azurerm_storage_container.dpkeys.name}/keys.xml"
    "DataProtection__KeyIdentifier"         = azurerm_key_vault_key.dp.versionless_id
    # Passwordless Azure SQL via the app's Managed Identity (no password anywhere).
    "ConnectionStrings__DefaultConnection" = "Server=tcp:${azurerm_mssql_server.main.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.main.name};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;"
  }
}

resource "azurerm_linux_web_app" "web" {
  name                = "app-${var.prefix}-web-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  service_plan_id     = azurerm_service_plan.main.id
  https_only          = true

  identity {
    type = "SystemAssigned"
  }

  site_config {
    minimum_tls_version = "1.2"
    ftps_state          = "Disabled"
    health_check_path   = "/health/ready"

    application_stack {
      docker_image_name   = "contoso-web:latest"
      docker_registry_url = "https://${azurerm_container_registry.main.login_server}"
    }
  }

  app_settings = local.common_app_settings
  tags         = local.tags
}

resource "azurerm_linux_web_app_slot" "web_staging" {
  name           = "staging"
  app_service_id = azurerm_linux_web_app.web.id
  https_only     = true

  identity {
    type = "SystemAssigned"
  }

  site_config {
    minimum_tls_version = "1.2"
    ftps_state          = "Disabled"
    health_check_path   = "/health/ready"

    application_stack {
      docker_image_name   = "contoso-web:latest"
      docker_registry_url = "https://${azurerm_container_registry.main.login_server}"
    }
  }

  app_settings = local.common_app_settings
  tags         = local.tags
}

resource "azurerm_linux_web_app" "api" {
  name                = "app-${var.prefix}-api-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  service_plan_id     = azurerm_service_plan.main.id
  https_only          = true

  identity {
    type = "SystemAssigned"
  }

  site_config {
    minimum_tls_version = "1.2"
    ftps_state          = "Disabled"
    health_check_path   = "/health/ready"

    application_stack {
      docker_image_name   = "contoso-api:latest"
      docker_registry_url = "https://${azurerm_container_registry.main.login_server}"
    }
  }

  app_settings = local.common_app_settings
  tags         = local.tags
}

resource "azurerm_linux_web_app_slot" "api_staging" {
  name           = "staging"
  app_service_id = azurerm_linux_web_app.api.id
  https_only     = true

  identity {
    type = "SystemAssigned"
  }

  site_config {
    minimum_tls_version = "1.2"
    ftps_state          = "Disabled"
    health_check_path   = "/health/ready"

    application_stack {
      docker_image_name   = "contoso-api:latest"
      docker_registry_url = "https://${azurerm_container_registry.main.login_server}"
    }
  }

  app_settings = local.common_app_settings
  tags         = local.tags
}

# ---------------------------------------------------------------------------
# Data: Azure SQL with Entra-only auth (no SQL password)
# ---------------------------------------------------------------------------
resource "azurerm_mssql_server" "main" {
  name                = "sql-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  version             = "12.0"

  azuread_administrator {
    login_username              = var.sql_admin_login
    object_id                   = var.sql_admin_object_id
    tenant_id                   = var.tenant_id
    azuread_authentication_only = true
  }

  tags = local.tags
}

resource "azurerm_mssql_database" "main" {
  name      = "sqldb-${local.name}"
  server_id = azurerm_mssql_server.main.id
  sku_name  = var.sku_sql_database
  tags      = local.tags
}

# Allow Azure services (App Service) to reach the server. Tighten with Private
# Endpoints in Phase 6.
resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

# ---------------------------------------------------------------------------
# Secrets + Data Protection key ring
# ---------------------------------------------------------------------------
resource "azurerm_key_vault" "main" {
  name                       = "kv-${local.name}"
  resource_group_name        = azurerm_resource_group.main.name
  location                   = azurerm_resource_group.main.location
  tenant_id                  = var.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  purge_protection_enabled   = true
  soft_delete_retention_days = 7
  tags                       = local.tags
}

# The deployer needs crypto rights to create the key (RBAC mode).
resource "azurerm_role_assignment" "deployer_kv_admin" {
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Administrator"
  principal_id         = var.deployer_object_id
}

resource "azurerm_key_vault_key" "dp" {
  name         = "dataprotection"
  key_vault_id = azurerm_key_vault.main.id
  key_type     = "RSA"
  key_size     = 2048
  key_opts     = ["wrapKey", "unwrapKey"]

  depends_on = [azurerm_role_assignment.deployer_kv_admin]
}

resource "azurerm_storage_account" "main" {
  name                     = "st${local.compact}"
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  min_tls_version          = "TLS1_2"
  tags                     = local.tags
}

resource "azurerm_storage_container" "dpkeys" {
  name                  = "dataprotection-keys"
  storage_account_id    = azurerm_storage_account.main.id
  container_access_type = "private"
}

# ---------------------------------------------------------------------------
# Passwordless access for the app identities (Managed Identity + RBAC)
# ---------------------------------------------------------------------------
locals {
  app_principals = {
    web         = azurerm_linux_web_app.web.identity[0].principal_id
    web_staging = azurerm_linux_web_app_slot.web_staging.identity[0].principal_id
    api         = azurerm_linux_web_app.api.identity[0].principal_id
    api_staging = azurerm_linux_web_app_slot.api_staging.identity[0].principal_id
  }
}

resource "azurerm_role_assignment" "acr_pull" {
  for_each             = local.app_principals
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "kv_secrets_user" {
  for_each             = local.app_principals
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Secrets User"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "kv_crypto_user" {
  for_each             = local.app_principals
  scope                = azurerm_key_vault.main.id
  role_definition_name = "Key Vault Crypto User"
  principal_id         = each.value
}

resource "azurerm_role_assignment" "blob_contributor" {
  for_each             = local.app_principals
  scope                = azurerm_storage_account.main.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = each.value
}

# ---------------------------------------------------------------------------
# React SPA -> Azure Static Web App
# ---------------------------------------------------------------------------
resource "azurerm_static_web_app" "spa" {
  name                = "stapp-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  location            = var.static_web_app_location
  sku_tier            = "Free"
  sku_size            = "Free"
  tags                = local.tags
}

# ---------------------------------------------------------------------------
# Edge: Front Door + WAF in front of the Web app
# ---------------------------------------------------------------------------
resource "azurerm_cdn_frontdoor_profile" "main" {
  name                = "afd-${local.name}"
  resource_group_name = azurerm_resource_group.main.name
  sku_name            = "Standard_AzureFrontDoor"
  tags                = local.tags
}

resource "azurerm_cdn_frontdoor_endpoint" "web" {
  name                     = "fde-${var.prefix}-web"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.main.id
  tags                     = local.tags
}

resource "azurerm_cdn_frontdoor_origin_group" "web" {
  name                     = "og-web"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.main.id

  load_balancing {
    sample_size                 = 4
    successful_samples_required = 3
  }

  health_probe {
    path                = "/health/ready"
    protocol            = "Https"
    request_type        = "GET"
    interval_in_seconds = 30
  }
}

resource "azurerm_cdn_frontdoor_origin" "web" {
  name                          = "origin-web"
  cdn_frontdoor_origin_group_id = azurerm_cdn_frontdoor_origin_group.web.id
  enabled                       = true

  host_name          = azurerm_linux_web_app.web.default_hostname
  origin_host_header = azurerm_linux_web_app.web.default_hostname
  http_port          = 80
  https_port         = 443
  priority           = 1
  weight             = 1000

  certificate_name_check_enabled = true
}

resource "azurerm_cdn_frontdoor_route" "web" {
  name                          = "route-web"
  cdn_frontdoor_endpoint_id     = azurerm_cdn_frontdoor_endpoint.web.id
  cdn_frontdoor_origin_group_id = azurerm_cdn_frontdoor_origin_group.web.id
  cdn_frontdoor_origin_ids      = [azurerm_cdn_frontdoor_origin.web.id]

  supported_protocols    = ["Http", "Https"]
  patterns_to_match      = ["/*"]
  forwarding_protocol    = "HttpsOnly"
  https_redirect_enabled = true
  link_to_default_domain = true
}

resource "azurerm_cdn_frontdoor_firewall_policy" "main" {
  name                = "waf${local.compact}"
  resource_group_name = azurerm_resource_group.main.name
  sku_name            = azurerm_cdn_frontdoor_profile.main.sku_name
  enabled             = true
  mode                = "Prevention"

  custom_rule {
    name     = "RateLimit"
    enabled  = true
    priority = 1
    type     = "RateLimitRule"
    action   = "Block"

    rate_limit_duration_in_minutes = 1
    rate_limit_threshold           = 1000

    match_condition {
      match_variable     = "RemoteAddr"
      operator           = "IPMatch"
      negation_condition = false
      match_values       = ["0.0.0.0/0"]
    }
  }

  tags = local.tags
}

resource "azurerm_cdn_frontdoor_security_policy" "main" {
  name                     = "secpol-${local.name}"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.main.id

  security_policies {
    firewall {
      cdn_frontdoor_firewall_policy_id = azurerm_cdn_frontdoor_firewall_policy.main.id

      association {
        domain {
          cdn_frontdoor_domain_id = azurerm_cdn_frontdoor_endpoint.web.id
        }
        patterns_to_match = ["/*"]
      }
    }
  }
}
