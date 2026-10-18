variable "name" {
  type        = string
  description = "Name of the resource group (CAF: rg-ulbraone-<env>)."
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
