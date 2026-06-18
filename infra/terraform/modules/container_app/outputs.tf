output "id" {
  value       = azurerm_container_app.this.id
  description = "Container App resource ID."
}

output "name" {
  value       = azurerm_container_app.this.name
  description = "Container App name."
}

output "fqdn" {
  value       = azurerm_container_app.this.ingress[0].fqdn
  description = "Ingress FQDN of the Container App."
}

output "latest_revision_fqdn" {
  value       = azurerm_container_app.this.latest_revision_fqdn
  description = "Latest revision FQDN."
}
