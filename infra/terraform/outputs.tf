output "resource_group_name" {
  value       = module.resource_group.name
  description = "Resource group name."
}

output "api_fqdn" {
  value       = module.container_app_api.fqdn
  description = "API Container App ingress FQDN."
}

output "web_fqdn" {
  value       = module.container_app_web.fqdn
  description = "Web Container App ingress FQDN."
}

output "postgres_fqdn" {
  value       = module.postgres.fqdn
  description = "PostgreSQL Flexible Server FQDN."
}

output "key_vault_uri" {
  value       = module.key_vault.vault_uri
  description = "Key Vault URI."
}

output "acr_login_server" {
  value       = data.azurerm_container_registry.shared.login_server
  description = "Shared ACR login server."
}

output "identity_client_id" {
  value       = module.identity.client_id
  description = "Managed identity client ID (for workload identity / SDK configuration)."
}

output "application_insights_connection_string" {
  value       = module.observability.application_insights_connection_string
  sensitive   = true
  description = "Application Insights connection string."
}

output "service_bus_namespace" {
  value       = "${local.sb_name}.servicebus.windows.net"
  description = "Service Bus namespace FQDN."
}
