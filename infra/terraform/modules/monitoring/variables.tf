variable "environment" {
  type        = string
  description = "Environment slug (dev/stage/prod) — used in resource names and in the workbook header."
}

variable "resource_group_name" {
  type        = string
  description = "Resource group to deploy the alerts and workbook into."
}

variable "location" {
  type        = string
  description = "Azure region for the workbook (action groups are always global)."
}

variable "application_insights_id" {
  type        = string
  description = "Application Insights resource ID — scope of the log alerts and fallback resource of the workbook."
}

variable "container_app_api_id" {
  type        = string
  description = "API Container App resource ID — scope of the CPU/memory/replica alerts."
}

variable "postgres_server_id" {
  type        = string
  description = "Postgres Flexible Server resource ID — scope of the database alerts."
}

variable "api_memory" {
  type        = string
  description = <<-EOT
    Memory limit of the API container, in the same "<n>Gi" spelling the container_app module
    takes — pass it the very same `local.env.api_memory`. Only used to derive the
    memory-pressure alert threshold, which is 85% of this.

    Taking the string rather than a number is deliberate: the previous `api_memory_gib` number
    defaulted to 0.5 and had to be kept in sync by hand, so raising the container to 1Gi (#232)
    would have silently left the alert firing at 456 MB. Same spelling at both call sites means
    they cannot drift.
  EOT

  validation {
    # Container Apps expresses memory as GiB (2 GiB per vCPU in the Consumption profile), so
    # anything else is a mistake — fail here rather than compute a nonsense threshold from it.
    condition     = can(tonumber(trimsuffix(var.api_memory, "Gi")))
    error_message = "api_memory must be a GiB value such as \"0.5Gi\" or \"1Gi\"."
  }
}

# ---------------------------------------------------------------------------
# Notification targets
# ---------------------------------------------------------------------------
#
# Phone numbers are personal data and never live in the repo. They arrive through
# TF_VAR_alert_sms_receivers (a GitHub Actions secret holding a JSON array), the same
# way ai_api_key and postgres_admin_login already do. An empty list is valid: the
# action group is still created, the alerts still fire and still show in the portal,
# they just do not page anyone.
variable "alert_sms_receivers" {
  type = list(object({
    name         = string
    country_code = string
    phone_number = string
  }))
  default     = []
  sensitive   = true
  description = <<-EOT
    SMS targets for alerts. Example (as TF_VAR_alert_sms_receivers):
    [{"name":"oncall","country_code":"55","phone_number":"51900000001"}]
    `name` must be unique, <= 12 chars, alphanumeric. `phone_number` carries no country
    code, no spaces and no punctuation.
  EOT
}

variable "alert_email_receivers" {
  type = list(object({
    name  = string
    email = string
  }))
  default     = []
  sensitive   = true
  description = "Email targets for alerts. Same shape and same delivery path as alert_sms_receivers."
}

# ---------------------------------------------------------------------------
# Thresholds
# ---------------------------------------------------------------------------
#
# Defaults are deliberately conservative: an alert nobody trusts is worse than no alert.
# Tune them from the workbook's own numbers after a couple of weeks of real traffic.
variable "http_5xx_threshold" {
  type        = number
  default     = 3
  description = "Number of 5xx responses in a 15-minute window that opens the availability alert."
}

variable "exception_threshold" {
  type        = number
  default     = 10
  description = "Number of exceptions in a 15-minute window that opens the exception-spike alert."
}

variable "latency_p95_threshold_ms" {
  type        = number
  default     = 3000
  description = "p95 request latency (ms) over 15 minutes that opens the latency alert. Excludes OPTIONS preflights."
}

variable "dependency_failure_threshold" {
  type        = number
  default     = 5
  description = "Failed dependency calls in a 15-minute window that opens the dependency alert. Benign codes are filtered in the query."
}

variable "enable_replica_alert" {
  type        = bool
  default     = true
  description = <<-EOT
    Alert when the API has no running replica. Must be false wherever min_replicas is 0 —
    in dev, scale-to-zero is the intended state and this alert would fire every idle hour.
  EOT
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Resource tags."
}
