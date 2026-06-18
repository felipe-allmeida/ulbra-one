output "id" {
  value       = azurerm_storage_account.this.id
  description = "Storage account resource ID."
}

output "name" {
  value       = azurerm_storage_account.this.name
  description = "Storage account name."
}

output "primary_blob_endpoint" {
  value       = azurerm_storage_account.this.primary_blob_endpoint
  description = "Primary blob service endpoint."
}
