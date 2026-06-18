variable "name" {
  type        = string
  description = "Flexible server name (CAF: psql-ulbraone-<env>)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to deploy into."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "administrator_login" {
  type        = string
  description = "Postgres admin username."
}

variable "administrator_password" {
  type        = string
  sensitive   = true
  description = "Postgres admin password (generated random_password in root)."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Resource tags."
}
