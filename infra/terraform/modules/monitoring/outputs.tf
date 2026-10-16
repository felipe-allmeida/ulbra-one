output "action_group_id" {
  value       = azurerm_monitor_action_group.this.id
  description = "Action group that every alert in this environment notifies."
}

output "workbook_id" {
  value       = azurerm_application_insights_workbook.dashboard.id
  description = "Resource ID of the environment dashboard workbook."
}
