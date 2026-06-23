variable "subscription_id" {
  type        = string
  description = "Target Azure subscription ID."
}

variable "tenant_id" {
  type        = string
  description = "Entra (Azure AD) tenant ID."
}

variable "prefix" {
  type        = string
  default     = "contoso-univ"
  description = "Naming prefix for resources."
}

variable "environment" {
  type        = string
  default     = "prod"
  description = "Environment name (e.g. prod, staging)."
}

variable "location" {
  type        = string
  default     = "eastus"
  description = "Primary Azure region."
}

variable "static_web_app_location" {
  type        = string
  default     = "eastus2"
  description = "Azure Static Web Apps is only available in a few regions."
}

variable "sql_admin_login" {
  type        = string
  description = "Entra principal (group or user) name set as the SQL Server AAD admin."
}

variable "sql_admin_object_id" {
  type        = string
  description = "Object ID of the Entra principal set as the SQL Server AAD admin."
}

variable "deployer_object_id" {
  type        = string
  description = "Object ID of the CI/CD deployer identity; granted Key Vault admin so it can create the Data Protection key."
}

variable "sku_app_service_plan" {
  type        = string
  default     = "P1v3"
  description = "Linux App Service Plan SKU."
}

variable "sku_sql_database" {
  type        = string
  default     = "S0"
  description = "Azure SQL Database SKU."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Extra tags merged onto every resource."
}
