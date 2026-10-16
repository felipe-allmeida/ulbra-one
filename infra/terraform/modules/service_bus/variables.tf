variable "name" {
  type        = string
  description = "Service Bus namespace name (CAF: sb-ulbraone-<env>)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to deploy into."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Resource tags."
}
