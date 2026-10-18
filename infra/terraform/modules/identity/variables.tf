variable "name" {
  type        = string
  description = "User-assigned identity name (CAF: id-ulbraone-api-<env>)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to deploy into."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "service_bus_namespace_id" {
  type        = string
  description = "Service Bus namespace resource ID for role assignment."
}

variable "storage_account_id" {
  type        = string
  description = "Storage account resource ID for role assignment."
}

variable "key_vault_id" {
  type        = string
  description = "Key Vault resource ID for role assignment."
}

variable "acr_id" {
  type        = string
  description = "Container registry resource ID for AcrPull role assignment."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Resource tags."
}
