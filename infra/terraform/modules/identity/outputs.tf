output "id" {
  value       = azurerm_user_assigned_identity.this.id
  description = "User-assigned identity resource ID."
}

output "principal_id" {
  value       = azurerm_user_assigned_identity.this.principal_id
  description = "Service principal (object) ID of the managed identity."
}

output "client_id" {
  value       = azurerm_user_assigned_identity.this.client_id
  description = "Client ID (app ID) of the managed identity."
}
