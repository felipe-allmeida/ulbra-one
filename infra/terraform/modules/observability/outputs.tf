output "log_analytics_workspace_id" {
  value       = azurerm_log_analytics_workspace.this.id
  description = "Log Analytics workspace resource ID."
}

output "log_analytics_workspace_primary_shared_key" {
  value       = azurerm_log_analytics_workspace.this.primary_shared_key
  sensitive   = true
  description = "Log Analytics workspace primary shared key."
}

output "log_analytics_workspace_customer_id" {
  value       = azurerm_log_analytics_workspace.this.workspace_id
  description = "Log Analytics workspace customer ID (GUID)."
}

output "application_insights_connection_string" {
  value       = azurerm_application_insights.this.connection_string
  sensitive   = true
  description = "Application Insights connection string."
}

output "application_insights_instrumentation_key" {
  value       = azurerm_application_insights.this.instrumentation_key
  sensitive   = true
  description = "Application Insights instrumentation key."
}
