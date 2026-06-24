output "id" {
  value       = azurerm_key_vault.this.id
  description = "Key Vault resource ID."
}

output "vault_uri" {
  value       = azurerm_key_vault.this.vault_uri
  description = "Key Vault URI (https://<name>.vault.azure.net/)."
}

output "secret_ids" {
  value       = { for k, v in azurerm_key_vault_secret.secrets : k => v.id }
  description = "Map of secret name => versionless secret ID."
}

output "secret_versionless_ids" {
  value       = { for k, v in azurerm_key_vault_secret.secrets : k => v.versionless_id }
  description = "Map of secret name => versionless secret URI (for container app secret references)."
}
