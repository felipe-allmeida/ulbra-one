variable "resource_group_name" {
  type        = string
  description = "Resource group to deploy into."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "log_analytics_workspace_name" {
  type        = string
  description = "Name for the Log Analytics workspace (CAF: log-ulbraone-<env>)."
}

variable "application_insights_name" {
  type        = string
  description = "Name for the Application Insights instance (CAF: appi-ulbraone-<env>)."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Resource tags."
}
