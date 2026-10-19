output "id" {
  value       = azurerm_postgresql_flexible_server.this.id
  description = "Flexible server resource ID."
}

output "fqdn" {
  value       = azurerm_postgresql_flexible_server.this.fqdn
  description = "Flexible server fully-qualified domain name."
}

output "server_name" {
  value       = azurerm_postgresql_flexible_server.this.name
  description = "Flexible server name."
}
