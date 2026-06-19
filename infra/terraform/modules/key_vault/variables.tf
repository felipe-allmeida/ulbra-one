variable "name" {
  type        = string
  description = "Key Vault name (CAF: kv-ulbraone-<env>)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to deploy into."
}

variable "location" {
  type        = string
  description = "Azure region."
}

variable "tenant_id" {
  type        = string
  description = "Azure AD tenant ID."
}

variable "deployer_object_id" {
  type        = string
  description = "Object ID of the principal running Terraform — granted Key Vault Secrets Officer so it can write the secrets below (RBAC-mode vault)."
}

variable "secrets" {
  type        = map(string)
  sensitive   = true
  description = "Map of secret name => secret value to store in Key Vault."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Resource tags."
}
