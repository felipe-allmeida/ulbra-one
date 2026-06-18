variable "environment" {
  type        = string
  description = "Deployment environment name (dev | prod). Used to compute all CAF resource names."
  validation {
    condition     = contains(["dev", "prod"], var.environment)
    error_message = "environment must be 'dev' or 'prod'."
  }
}

variable "location" {
  type        = string
  default     = "eastus2"
  description = "Azure region for all resources."
}

variable "postgres_admin_login" {
  type        = string
  description = "Administrator login for the PostgreSQL Flexible Server."
}

variable "api_image" {
  type        = string
  description = "Full container image reference for the API (e.g. crulbraone.azurecr.io/api:latest)."
}

variable "web_image" {
  type        = string
  description = "Full container image reference for the web frontend (e.g. crulbraone.azurecr.io/web:latest)."
}

variable "google_client_id" {
  type        = string
  default     = ""
  description = "Google OAuth client ID exposed to the web frontend as VITE_GOOGLE_CLIENT_ID."
}

variable "api_min_replicas" {
  type        = number
  default     = 0
  description = "Minimum replica count for the API container app."
}

variable "api_max_replicas" {
  type        = number
  default     = 2
  description = "Maximum replica count for the API container app."
}

variable "web_min_replicas" {
  type        = number
  default     = 0
  description = "Minimum replica count for the web container app."
}

variable "web_max_replicas" {
  type        = number
  default     = 2
  description = "Maximum replica count for the web container app."
}

variable "jwt_secret" {
  type        = string
  sensitive   = true
  description = "JWT signing secret. Stored in Key Vault as 'Authentication--JwtSecret'."
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Common tags applied to all resources."
}

# ---------------------------------------------------------------------------
# CAF-computed names (locals in main.tf reference var.environment)
# ---------------------------------------------------------------------------
locals {
  rg_name          = "rg-ulbraone-${var.environment}"
  cae_name         = "cae-ulbraone-${var.environment}"
  ca_api_name      = "ca-ulbraone-api-${var.environment}"
  ca_web_name      = "ca-ulbraone-web-${var.environment}"
  psql_name        = "psql-ulbraone-${var.environment}"
  sb_name          = "sb-ulbraone-${var.environment}"
  storage_name     = "stulbraone${var.environment}" # no dashes, <=24 chars
  kv_name          = "kv-ulbraone-${var.environment}"
  identity_name    = "id-ulbraone-api-${var.environment}"
  log_name         = "log-ulbraone-${var.environment}"
  appi_name        = "appi-ulbraone-${var.environment}"

  # ACR is shared across environments — created by bootstrap (Task C3)
  acr_name    = "crulbraone"
  acr_rg_name = "rg-ulbraone-tfstate"

  common_tags = merge(var.tags, {
    environment = var.environment
    managed_by  = "terraform"
    project     = "ulbra-one"
  })
}
