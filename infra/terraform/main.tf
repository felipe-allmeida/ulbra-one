# ---------------------------------------------------------------------------
# Data sources
# ---------------------------------------------------------------------------

# Current Azure subscription / tenant — used for Key Vault tenant_id
data "azurerm_client_config" "current" {}

# Shared ACR created by the bootstrap script (Task C3).
# Decision: reference via data source (shared registry, single namespace).
# Rationale: ACR is environment-agnostic; sharing avoids per-env quota waste
# and allows the same image digest to be promoted from dev to prod.
data "azurerm_container_registry" "shared" {
  name                = local.acr_name
  resource_group_name = local.acr_rg_name
}

# ---------------------------------------------------------------------------
# Postgres admin password — generated once per workspace, stored in Key Vault
# ---------------------------------------------------------------------------

resource "random_password" "postgres" {
  length           = 32
  special          = true
  override_special = "!#$%&*()-_=+[]{}<>:?"
}

# ---------------------------------------------------------------------------
# Modules — instantiated in dependency order
# ---------------------------------------------------------------------------

module "resource_group" {
  source   = "./modules/resource_group"
  name     = local.rg_name
  location = var.location
  tags     = local.common_tags
}

module "observability" {
  source              = "./modules/observability"
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location

  log_analytics_workspace_name = local.log_name
  application_insights_name    = local.appi_name
  tags                         = local.common_tags
}

module "service_bus" {
  source              = "./modules/service_bus"
  name                = local.sb_name
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location
  tags                = local.common_tags
}

module "storage" {
  source              = "./modules/storage"
  name                = local.storage_name
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location
  tags                = local.common_tags
}

module "postgres" {
  source              = "./modules/postgres"
  name                = local.psql_name
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location

  administrator_login    = var.postgres_admin_login
  administrator_password = random_password.postgres.result
  sku_name               = var.postgres_sku_name
  tags                   = local.common_tags
}

# Key Vault — needs postgres FQDN + password to build the connection string secret
module "key_vault" {
  source              = "./modules/key_vault"
  name                = local.kv_name
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location
  tenant_id           = data.azurerm_client_config.current.tenant_id

  secrets = {
    # Secret name follows ASP.NET Core config hierarchy separator "--"
    "Authentication--JwtSecret" = var.jwt_secret

    # Full ADO connection string for EF Core / Npgsql
    "ConnectionStrings--Default" = join(";", [
      "Host=${module.postgres.fqdn}",
      "Port=5432",
      "Database=ulbra_one",
      "Username=${var.postgres_admin_login}",
      "Password=${random_password.postgres.result}",
      "Ssl Mode=Require",
      "Trust Server Certificate=true",
    ])
  }

  tags = local.common_tags
}

# Identity — depends on KV, SB, Storage, ACR IDs
module "identity" {
  source              = "./modules/identity"
  name                = local.identity_name
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location

  service_bus_namespace_id = module.service_bus.id
  storage_account_id       = module.storage.id
  key_vault_id             = module.key_vault.id
  acr_id                   = data.azurerm_container_registry.shared.id
  tags                     = local.common_tags
}

# Container Apps Environment
module "container_app_env" {
  source              = "./modules/container_app_env"
  name                = local.cae_name
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location

  log_analytics_workspace_id = module.observability.log_analytics_workspace_id
  tags                       = local.common_tags
}

# API Container App
module "container_app_api" {
  source = "./modules/container_app"

  name                         = local.ca_api_name
  resource_group_name          = module.resource_group.name
  container_app_environment_id = module.container_app_env.id

  image        = var.api_image
  target_port  = 8080
  min_replicas = var.api_min_replicas
  max_replicas = var.api_max_replicas

  identity_id      = module.identity.id
  acr_login_server = data.azurerm_container_registry.shared.login_server

  # Plain-text env vars
  env_vars = {
    ASPNETCORE_ENVIRONMENT                = var.environment == "prod" ? "Production" : "Development"
    APPLICATIONINSIGHTS_CONNECTION_STRING = module.observability.application_insights_connection_string
    Azure__ServiceBus__Namespace          = "${local.sb_name}.servicebus.windows.net"
    Azure__Storage__AccountName           = local.storage_name
  }

  # Key Vault–backed secrets injected as env vars
  secret_env_vars = {
    "Authentication--JwtSecret"  = module.key_vault.secret_versionless_ids["Authentication--JwtSecret"]
    "ConnectionStrings--Default" = module.key_vault.secret_versionless_ids["ConnectionStrings--Default"]
  }

  tags = local.common_tags

  depends_on = [module.identity]
}

# Web (frontend) Container App
module "container_app_web" {
  source = "./modules/container_app"

  name                         = local.ca_web_name
  resource_group_name          = module.resource_group.name
  container_app_environment_id = module.container_app_env.id

  image        = var.web_image
  target_port  = 80
  min_replicas = var.web_min_replicas
  max_replicas = var.web_max_replicas

  identity_id      = module.identity.id
  acr_login_server = data.azurerm_container_registry.shared.login_server

  # Frontend only needs VITE_ vars — no secrets required
  env_vars = {
    VITE_API_BASE_URL     = "https://${module.container_app_api.fqdn}"
    VITE_GOOGLE_CLIENT_ID = var.google_client_id
  }

  secret_env_vars = {}

  tags = local.common_tags

  depends_on = [module.identity, module.container_app_api]
}
