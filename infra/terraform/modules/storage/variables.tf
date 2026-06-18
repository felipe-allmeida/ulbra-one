variable "name" {
  type        = string
  description = "Storage account name (CAF: stulbraone<env> — lowercase, no dashes, <=24 chars)."
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
