output "id" {
  value       = azurerm_container_app_environment.this.id
  description = "Container Apps Environment resource ID."
}

output "name" {
  value       = azurerm_container_app_environment.this.name
  description = "Container Apps Environment name."
}

output "default_domain" {
  value       = azurerm_container_app_environment.this.default_domain
  description = "Default domain for the environment."
}
