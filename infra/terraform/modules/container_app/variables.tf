variable "name" {
  type        = string
  description = "Container App name (CAF: ca-ulbraone-api-<env> or ca-ulbraone-web-<env>)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to deploy into."
}

variable "container_app_environment_id" {
  type        = string
  description = "Container Apps Environment resource ID."
}

variable "image" {
  type        = string
  description = "Container image reference (e.g. crulbraone.azurecr.io/api:latest)."
}

variable "target_port" {
  type        = number
  description = "Port the container listens on."
}

variable "min_replicas" {
  type        = number
  default     = 0
  description = "Minimum replica count (0 = scale-to-zero)."
}

variable "max_replicas" {
  type        = number
  default     = 2
  description = "Maximum replica count."
}

variable "identity_id" {
  type        = string
  description = "User-assigned managed identity resource ID."
}

variable "acr_login_server" {
  type        = string
  description = "ACR login server FQDN (e.g. crulbraone.azurecr.io)."
}

variable "env_vars" {
  type        = map(string)
  default     = {}
  description = "Plain-text environment variables (name => value)."
}

variable "secret_env_vars" {
  type        = map(string)
  default     = {}
  sensitive   = true
  description = "Map of env var name => Key Vault secret versionless URI. Injected as secrets + env refs."
}

variable "cpu" {
  type        = string
  default     = "0.25"
  description = "CPU cores allocated to the container."
}

variable "memory" {
  type        = string
  default     = "0.5Gi"
  description = "Memory allocated to the container."
}

variable "revision_mode" {
  type        = string
  default     = "Single"
  description = "Revision mode: Single or Multiple."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Resource tags."
}
