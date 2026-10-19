output "id" {
  value       = azurerm_container_registry.this.id
  description = "ACR resource ID."
}

output "login_server" {
  value       = azurerm_container_registry.this.login_server
  description = "ACR login server FQDN (e.g. crulbraone.azurecr.io)."
}

output "name" {
  value       = azurerm_container_registry.this.name
  description = "ACR name."
}
