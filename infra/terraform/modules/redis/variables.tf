variable "name" {
  type        = string
  description = "Redis cache name (CAF: redis-ulbraone-<env>)."
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

variable "sku_name" {
  type        = string
  default     = "Balanced_B0"
  description = "Azure Managed Redis SKU (Balanced_B0 = 0,5 GB, o menor disponível)."
}

variable "high_availability_enabled" {
  type        = bool
  default     = true
  description = "Réplica + SLA. Desligar apenas em dev/test — não pode ser desligado depois da criação."
}
