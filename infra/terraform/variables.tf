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

variable "web_custom_domain" {
  type        = string
  default     = ""
  description = "Custom domain bound to the web app (e.g. dev.one.ulbra.ai). Added to API + blob CORS so the browser on that domain is allowed. Empty = only the default *.azurecontainerapps.io origin."
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

variable "enable_remittance_sweep_job" {
  type        = bool
  default     = true
  description = "Provision the daily CNAB remittance-sweep Container App Job. Set false to disable in an environment (e.g. dev)."
}

variable "remittance_sweep_cron" {
  type        = string
  default     = "0 9 * * *"
  description = "Cron (UTC) for the remittance-sweep job. Default 09:00 UTC = 06:00 BRT (horário de corte diário)."
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

variable "postgres_sku_name" {
  type        = string
  default     = "B_Standard_B1ms"
  description = "PostgreSQL Flexible Server SKU. Override to scale vertically (e.g. GP_Standard_D2s_v3)."
}

variable "jwt_secret" {
  type        = string
  sensitive   = true
  description = "JWT signing secret. Stored in Key Vault as 'Authentication--JwtSecret'."
}

variable "ai_api_key" {
  type        = string
  sensitive   = true
  description = "Anthropic API key for the payable AI classifier. Stored in Key Vault as 'Ai--ApiKey'. Supply via -var / TF_VAR_ai_api_key; never commit the value."
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
  rg_name     = "rg-ulbraone-${var.environment}"
  cae_name    = "cae-ulbraone-${var.environment}"
  ca_api_name = "ca-ulbraone-api-${var.environment}"
  ca_web_name = "ca-ulbraone-web-${var.environment}"

  # Container App Job (scheduled) — daily CNAB remittance sweep.
  caj_remittance_sweep_name = "caj-ulbraone-remittance-sweep-${var.environment}"
  psql_name                 = "psql-ulbraone-${var.environment}"
  sb_name                   = "sb-ulbraone-${var.environment}"
  storage_name              = "stulbraone${var.environment}" # no dashes, <=24 chars
  kv_name                   = "kv-ulbraone-${var.environment}"
  identity_name             = "id-ulbraone-api-${var.environment}"
  log_name                  = "log-ulbraone-${var.environment}"
  appi_name                 = "appi-ulbraone-${var.environment}"

  # ACR is shared across environments — created by bootstrap (Task C3)
  acr_name    = "crulbraone"
  acr_rg_name = "rg-ulbraone-tfstate"

  # Browser origins allowed by API CORS + blob CORS: the default web FQDN plus any custom domain.
  web_origins = compact(concat(
    ["https://${local.ca_web_name}.${module.container_app_env.default_domain}"],
    var.web_custom_domain != "" ? ["https://${var.web_custom_domain}"] : [],
  ))
  # Indexed env vars (AllowedOrigins__0, __1, …) bind to the app's AllowedOrigins string[] config.
  cors_env_vars = { for i, o in local.web_origins : "AllowedOrigins__${i}" => o }

  common_tags = merge(var.tags, {
    environment = var.environment
    managed_by  = "terraform"
    project     = "ulbra-one"
  })
}
