locals {
  # Normalise secret env var names to valid container app secret names (lowercase alnum + hyphens).
  # Mirrors the container_app module so the same Key Vault URIs map identically.
  secret_names = {
    for k, v in var.secret_env_vars :
    k => replace(replace(lower(replace(replace(k, "__", "-"), "_", "-")), "--", "-"), "--", "-")
  }
}

# Azure Container Apps scheduled Job — runs the container to completion on a cron and exits.
# Used for the daily CNAB remittance sweep (`--job remittance-sweep`). Reuses the API image and the
# API's user-assigned identity; has no ingress (unlike the container_app module, which is why this is
# a separate resource/module).
resource "azurerm_container_app_job" "this" {
  name                         = var.name
  resource_group_name          = var.resource_group_name
  location                     = var.location
  container_app_environment_id = var.container_app_environment_id
  tags                         = var.tags

  replica_timeout_in_seconds = var.replica_timeout_in_seconds
  replica_retry_limit        = var.replica_retry_limit

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  registry {
    server   = var.acr_login_server
    identity = var.identity_id
  }

  # Key Vault–backed secrets (each references a KV secret via the managed identity).
  dynamic "secret" {
    for_each = var.secret_env_vars
    content {
      name                = local.secret_names[secret.key]
      key_vault_secret_id = secret.value
      identity            = var.identity_id
    }
  }

  schedule_trigger_config {
    cron_expression          = var.cron_expression
    parallelism              = var.parallelism
    replica_completion_count = var.replica_completion_count
  }

  template {
    container {
      name   = var.name
      image  = var.image
      cpu    = var.cpu
      memory = var.memory
      args   = var.args

      # Plain-text env vars.
      dynamic "env" {
        for_each = var.env_vars
        content {
          name  = env.key
          value = env.value
        }
      }

      # Secret-backed env vars (reference the secret block above).
      dynamic "env" {
        for_each = var.secret_env_vars
        content {
          name        = env.key
          secret_name = local.secret_names[env.key]
        }
      }
    }
  }
}
