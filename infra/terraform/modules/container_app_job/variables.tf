variable "name" {
  type        = string
  description = "Container App Job name (CAF: caj-ulbraone-<job>-<env>)."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to deploy into."
}

variable "location" {
  type        = string
  description = "Azure region (Container App Jobs require an explicit location)."
}

variable "container_app_environment_id" {
  type        = string
  description = "Container Apps Environment resource ID (shared with the API/web apps)."
}

variable "image" {
  type        = string
  description = "Container image reference — reuse the API image (e.g. crulbraone.azurecr.io/api:<sha>)."
}

variable "args" {
  type        = list(string)
  description = "Container args — selects the run-once job (e.g. [\"--job\",\"remittance-sweep\"])."
}

variable "cron_expression" {
  type        = string
  description = "Cron schedule (UTC) for the job trigger, e.g. \"0 20 * * *\" (17:00 BRT — Brasília is UTC-3)."
}

variable "identity_id" {
  type        = string
  description = "User-assigned managed identity resource ID (reuse the API identity — already has KV Secrets User + Storage Blob Data Contributor + AcrPull)."
}

variable "acr_login_server" {
  type        = string
  description = "ACR login server FQDN (e.g. crulbraone.azurecr.io)."
}

variable "env_vars" {
  type        = map(string)
  default     = {}
  description = "Plain-text environment variables (name => value). Keys use the ASP.NET \"__\" separator."
}

variable "secret_env_vars" {
  type        = map(string)
  default     = {}
  sensitive   = true
  description = "Map of env var name => Key Vault secret versionless URI. Injected as secrets + env refs."
}

variable "cpu" {
  type        = string
  default     = "0.5"
  description = "CPU cores allocated to the job container."
}

variable "memory" {
  type        = string
  default     = "1Gi"
  description = "Memory allocated to the job container."
}

variable "replica_timeout_in_seconds" {
  type        = number
  default     = 1800
  description = "Max seconds a job replica may run before it is terminated (30 min default)."
}

variable "replica_retry_limit" {
  type        = number
  default     = 1
  description = "Retries for a failed replica before the execution is marked failed."
}

variable "parallelism" {
  type        = number
  default     = 1
  description = "Number of replicas to run per execution."
}

variable "replica_completion_count" {
  type        = number
  default     = 1
  description = "Replicas that must complete successfully for the execution to succeed."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Resource tags."
}
