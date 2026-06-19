output "id" {
  value       = azurerm_servicebus_namespace.this.id
  description = "Service Bus namespace resource ID."
}

output "name" {
  value       = azurerm_servicebus_namespace.this.name
  description = "Service Bus namespace name."
}

output "endpoint" {
  value       = azurerm_servicebus_namespace.this.endpoint
  description = "Service Bus namespace endpoint."
}
