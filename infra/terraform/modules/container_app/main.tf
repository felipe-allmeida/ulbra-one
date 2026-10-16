locals {
  # Normalise secret env var names to valid container app secret names.
  # Container App secret names must be lowercase alphanumeric + hyphens only;
  # double-hyphens are collapsed to a single hyphen.
  secret_names = {
    for k, v in var.secret_env_vars :
    k => replace(replace(lower(replace(replace(k, "__", "-"), "_", "-")), "--", "-"), "--", "-")
  }
}

resource "azurerm_container_app" "this" {
  name                         = var.name
  container_app_environment_id = var.container_app_environment_id
  resource_group_name          = var.resource_group_name
  revision_mode                = var.revision_mode
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  registry {
    server   = var.acr_login_server
    identity = var.identity_id
  }

  # Key Vault–backed secrets (each references a KV secret via managed identity)
  dynamic "secret" {
    for_each = var.secret_env_vars
    content {
      name                = local.secret_names[secret.key]
      key_vault_secret_id = secret.value
      identity            = var.identity_id
    }
  }

  template {
    min_replicas = var.min_replicas
    max_replicas = var.max_replicas

    # Sem regra explícita o Container Apps usa concorrência HTTP (10 req/réplica). Uma conexão SSE é
    # uma requisição que nunca termina, então essa regra passaria a medir browsers abertos em vez de
    # carga — e grudaria a app em max_replicas. Ver §10 do design.
    dynamic "custom_scale_rule" {
      for_each = var.cpu_scale_threshold == null ? [] : [var.cpu_scale_threshold]
      content {
        name             = "cpu"
        custom_rule_type = "cpu"
        metadata = {
          type  = "Utilization"
          value = tostring(custom_scale_rule.value)
        }
      }
    }

    container {
      name   = var.name
      image  = var.image
      cpu    = var.cpu
      memory = var.memory

      # Plain-text env vars
      dynamic "env" {
        for_each = var.env_vars
        content {
          name  = env.key
          value = env.value
        }
      }

      # Secret-backed env vars (reference the secret block above)
      dynamic "env" {
        for_each = var.secret_env_vars
        content {
          name        = env.key
          secret_name = local.secret_names[env.key]
        }
      }
    }
  }

  ingress {
    external_enabled = true
    target_port      = var.target_port

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }
}
