variable "name" {
  type        = string
  description = "Container Apps Environment name (CAF: cae-ulbraone-<env>)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to deploy into."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "log_analytics_workspace_id" {
  type        = string
  description = "Log Analytics workspace resource ID for diagnostic logs."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Resource tags."
}
