output "resource_group" {
  value       = azurerm_resource_group.main.name
  description = "Resource group containing all resources."
}

output "acr_login_server" {
  value       = azurerm_container_registry.main.login_server
  description = "ACR login server for tagging/pushing images."
}

output "web_app_name" {
  value = azurerm_linux_web_app.web.name
}

output "web_app_default_hostname" {
  value = azurerm_linux_web_app.web.default_hostname
}

output "api_app_name" {
  value = azurerm_linux_web_app.api.name
}

output "api_app_default_hostname" {
  value = azurerm_linux_web_app.api.default_hostname
}

output "key_vault_uri" {
  value = azurerm_key_vault.main.vault_uri
}

output "sql_server_fqdn" {
  value = azurerm_mssql_server.main.fully_qualified_domain_name
}

output "sql_database_name" {
  value = azurerm_mssql_database.main.name
}

output "frontdoor_endpoint_hostname" {
  value       = azurerm_cdn_frontdoor_endpoint.web.host_name
  description = "Front Door hostname routing to the Web app."
}

output "static_web_app_hostname" {
  value = azurerm_static_web_app.spa.default_host_name
}

# App identities — feed these into the SQL `CREATE USER ... FROM EXTERNAL PROVIDER` step.
output "web_app_principal_id" {
  value = azurerm_linux_web_app.web.identity[0].principal_id
}

output "api_app_principal_id" {
  value = azurerm_linux_web_app.api.identity[0].principal_id
}
