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
# JWT signing secret — gerado por workspace, igual à senha do Postgres
# ---------------------------------------------------------------------------
#
# Nenhum humano precisa conhecer este valor: só a própria API assina e valida com ele (HS256).
# Gerar aqui, em vez de receber por var.jwt_secret, resolve três coisas de uma vez:
#   • garante um secret DISTINTO por ambiente — antes, o prod cairia no GitHub Secret JWT_SECRET
#     de nível repo, que é o mesmo do dev, e um token emitido no dev valeria em produção;
#   • tira o segredo dos .tfvars e do gerenciador de senhas;
#   • some com o passo manual de gerar e colar na hora do apply.
#
# Sem caracteres especiais: o valor viaja como env var no container, e escaping aqui só rende dor.
# 64 chars alfanuméricos ≈ 380 bits de entropia, muito acima do mínimo de 256 do HS256.
#
# Rotação: `terraform taint random_password.jwt` + apply. Invalida todos os tokens em circulação
# daquele ambiente — os usuários refazem login.
resource "random_password" "jwt" {
  length  = 64
  special = false
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

module "redis" {
  source              = "./modules/redis"
  name                = local.redis_name
  location            = var.redis_location
  resource_group_name = module.resource_group.name

  high_availability_enabled = local.env.redis_high_availability
  tags                      = local.common_tags
}

module "storage" {
  source              = "./modules/storage"
  name                = local.storage_name
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location
  tags                = local.common_tags

  # Allow the web app origins to PUT/GET blobs directly from the browser (fiscal SAS upload):
  # the default *.azurecontainerapps.io FQDN plus any custom domain (see local.web_origins).
  cors_allowed_origins = local.web_origins
}

module "postgres" {
  source              = "./modules/postgres"
  name                = local.psql_name
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location

  administrator_login    = var.postgres_admin_login
  administrator_password = random_password.postgres.result
  sku_name               = local.env.postgres_sku
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
    "Authentication--JwtSecret" = random_password.jwt.result

    # Anthropic API key for the payable AI classifier (app reads Ai:ApiKey).
    "Ai--ApiKey" = var.ai_api_key

    # GCP service-account key for the Orçamento importer to read the source Firestore. Empty unless a
    # migration run is planned (enable_orcamento_import); the importer reads Firestore:CredentialsJson.
    "Firestore--CredentialsJson" = var.firestore_credentials_json

    # Backplane de realtime (app reads Redis:ConnectionString). Único segredo do sistema fora do
    # fluxo de managed identity — ver o porquê em modules/redis/main.tf (§11.2 do design).
    "Redis--ConnectionString" = module.redis.connection_string

    # Full ADO connection string for EF Core / Npgsql (app reads ConnectionStrings:Database)
    "ConnectionStrings--Database" = join(";", [
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
  min_replicas = local.env.min_replicas
  max_replicas = var.api_max_replicas

  # Uma conexão SSE nunca fecha, então a regra HTTP default (10 req/réplica) mediria browsers
  # abertos em vez de carga. Ver o comentário no módulo container_app para o mecanismo. Só stage
  # e prod recebem o valor (70) — dev fica com null e mantém a HTTP rule default de propósito;
  # ver o comentário em locals.per_env.dev para o porquê (min_replicas=0 lá + CPU rule não
  # escala a partir de zero).
  cpu_scale_threshold = local.env.api_cpu_scale_threshold

  # Dimensionado por ambiente em locals.per_env — ver o comentário lá para as medições.
  # O app web segue nos defaults do módulo (0.25/0.5Gi): usa 70 MB de 512 e não cresce.
  cpu    = local.env.api_cpu
  memory = local.env.api_memory

  identity_id      = module.identity.id
  acr_login_server = data.azurerm_container_registry.shared.login_server

  # Plain-text env vars. Keys use the ASP.NET Core "__" hierarchy separator so they
  # bind to the same config sections the app reads (MassTransit:*, ObjectStorage:*).
  # cors_env_vars adds AllowedOrigins__0/__1/… (web FQDN + custom domain) so the API
  # accepts cross-origin requests from the SPA in every environment.
  env_vars = merge({
    ASPNETCORE_ENVIRONMENT                = local.env.aspnetcore_env
    APPLICATIONINSIGHTS_CONNECTION_STRING = module.observability.application_insights_connection_string

    # DefaultAzureCredential must target THIS user-assigned identity (Service Bus + Blob).
    AZURE_CLIENT_ID = module.identity.client_id

    # Messaging → Azure Service Bus, passwordless. MassTransit appends ".servicebus.windows.net".
    MassTransit__Transport = "AzureServiceBus"
    MassTransit__Namespace = local.sb_name

    # Object storage → Azure Blob, passwordless via managed identity.
    ObjectStorage__Provider   = "AzureBlob"
    ObjectStorage__AccountUrl = "https://${local.storage_name}.blob.core.windows.net"
    ObjectStorage__Bucket     = "attachments"

    # Data Protection key ring, shared by every replica. API only: the jobs build the same host but
    # never issue a cookie or antiforgery token.
    DataProtection__BlobUri = "https://${local.storage_name}.blob.core.windows.net/${module.storage.dataprotection_container_name}/keys.xml"

    # Google sign-in: backend validates the id_token audience against this client id.
    # A allow-list de domínios e os bootstrap admins entram via local.google_env_vars (abaixo) —
    # eles só existem em appsettings.Development.json, que não carrega fora de Development.
    Authentication__Google__ClientId = var.google_client_id

    # Payable AI classifier → real Anthropic provider (key injected via secret_env_vars below).
    Ai__Provider = "Anthropic"

    # Suspense account for an incurral whose expense account is still unknown (a requisition payable
    # schedules with the AI candidate as a mere suggestion). Without this the incurral parks and posts
    # nothing, while the settlement posts anyway — leaving Fornecedores a Pagar debited with no matching
    # credit. The account must exist and be postable in the chart; if it is missing the posting service
    # falls back to parking, so a bad value degrades instead of breaking.
    Accounting__UnclassifiedExpenseCode = "1.2.35.20.050"
  }, local.cors_env_vars, local.google_env_vars)

  # Key Vault–backed secrets injected as env vars (names match app config keys).
  secret_env_vars = {
    "Authentication__JwtSecret"   = module.key_vault.secret_versionless_ids["Authentication--JwtSecret"]
    "ConnectionStrings__Database" = module.key_vault.secret_versionless_ids["ConnectionStrings--Database"]
    "Ai__ApiKey"                  = module.key_vault.secret_versionless_ids["Ai--ApiKey"]
    "Redis__ConnectionString"     = module.key_vault.secret_versionless_ids["Redis--ConnectionString"]
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
  min_replicas = local.env.min_replicas
  max_replicas = var.web_max_replicas

  identity_id      = module.identity.id
  acr_login_server = data.azurerm_container_registry.shared.login_server

  # Frontend only needs VITE_ vars — no secrets required.
  # The API mounts every endpoint under /api (app.MapGroup("/api")), so the SPA base URL must
  # include it — otherwise calls like /api/auth/google resolve to /auth/google → 404.
  env_vars = {
    VITE_API_BASE_URL     = "https://${module.container_app_api.fqdn}/api"
    VITE_GOOGLE_CLIENT_ID = var.google_client_id
    VITE_ENVIRONMENT      = local.env.vite_env
  }

  secret_env_vars = {}

  tags = local.common_tags

  depends_on = [module.identity, module.container_app_api]
}

# Remittance-sweep Container App Job (scheduled) — daily CNAB remittance generation.
# Reuses the API image (`--job remittance-sweep`) and the API's user-assigned identity (already has
# KV Secrets User + Storage Blob Data Contributor + AcrPull). Kept as a scheduled ACA Job — not the
# in-process PeriodicTimer — because the API scales to zero, so an in-process timer would not fire
# reliably. The in-process RemittanceSweepJob stays disabled (RemittanceSweep__Enabled=false).
module "remittance_sweep_job" {
  count  = var.enable_remittance_sweep_job ? 1 : 0
  source = "./modules/container_app_job"

  name                         = local.caj_remittance_sweep_name
  resource_group_name          = module.resource_group.name
  location                     = module.resource_group.location
  container_app_environment_id = module.container_app_env.id

  image            = var.api_image
  args             = ["--job", "remittance-sweep"]
  cron_expression  = var.remittance_sweep_cron
  identity_id      = module.identity.id
  acr_login_server = data.azurerm_container_registry.shared.login_server

  # Same runtime config the API uses (the job builds the same host), minus web-only vars (CORS/Google).
  env_vars = {
    ASPNETCORE_ENVIRONMENT                = local.env.aspnetcore_env
    APPLICATIONINSIGHTS_CONNECTION_STRING = module.observability.application_insights_connection_string
    AZURE_CLIENT_ID                       = module.identity.client_id

    MassTransit__Transport = "AzureServiceBus"
    MassTransit__Namespace = local.sb_name

    ObjectStorage__Provider   = "AzureBlob"
    ObjectStorage__AccountUrl = "https://${local.storage_name}.blob.core.windows.net"
    ObjectStorage__Bucket     = "attachments"

    Ai__Provider = "Anthropic"

    # Same suspense account as the API: the job process resolves the same posting config, so the two must
    # not disagree about where an unclassified incurral lands.
    Accounting__UnclassifiedExpenseCode = "1.2.35.20.050"

    # The scheduled job IS the sweep; keep the in-process PeriodicTimer off inside the job process.
    RemittanceSweep__Enabled = "false"
  }

  secret_env_vars = {
    "Authentication__JwtSecret"   = module.key_vault.secret_versionless_ids["Authentication--JwtSecret"]
    "ConnectionStrings__Database" = module.key_vault.secret_versionless_ids["ConnectionStrings--Database"]
    "Ai__ApiKey"                  = module.key_vault.secret_versionless_ids["Ai--ApiKey"]
  }

  tags = local.common_tags

  depends_on = [module.identity]
}

# Requisition SLA escalation Container App Job (scheduled) — AEL-872. Runs as a scheduled ACA Job
# (`--job requisition-sla-sweep`): the sweep only DETECTS overdue stages and PUBLISHES RequisitionSlaBreached
# (EF outbox); a live API replica delivers it and the Notifications consumer materializes the alert.
module "requisition_sla_job" {
  count  = var.enable_requisition_sla_job ? 1 : 0
  source = "./modules/container_app_job"

  name                         = local.caj_requisition_sla_name
  resource_group_name          = module.resource_group.name
  location                     = module.resource_group.location
  container_app_environment_id = module.container_app_env.id

  image            = var.api_image
  args             = ["--job", "requisition-sla-sweep"]
  cron_expression  = var.requisition_sla_cron
  identity_id      = module.identity.id
  acr_login_server = data.azurerm_container_registry.shared.login_server

  # Same runtime config the API uses (the job builds the same host), minus web-only vars (CORS/Google).
  env_vars = {
    ASPNETCORE_ENVIRONMENT                = local.env.aspnetcore_env
    APPLICATIONINSIGHTS_CONNECTION_STRING = module.observability.application_insights_connection_string
    AZURE_CLIENT_ID                       = module.identity.client_id

    MassTransit__Transport = "AzureServiceBus"
    MassTransit__Namespace = local.sb_name

    ObjectStorage__Provider   = "AzureBlob"
    ObjectStorage__AccountUrl = "https://${local.storage_name}.blob.core.windows.net"
    ObjectStorage__Bucket     = "attachments"

    # No Ai__* here: the SLA sweep is deterministic (reads requisitions, publishes an event) and never calls
    # the AI provider — so it doesn't get the Anthropic key (least privilege). Do NOT copy it from the
    # remittance job. Thresholds come from appsettings (RequisitionSla:TriageHours/ApprovalHours).
  }

  secret_env_vars = {
    "Authentication__JwtSecret"   = module.key_vault.secret_versionless_ids["Authentication--JwtSecret"]
    "ConnectionStrings__Database" = module.key_vault.secret_versionless_ids["ConnectionStrings--Database"]
  }

  tags = local.common_tags

  depends_on = [module.identity]
}

# Firestore importer Container App (Azure Functions isolated, HTTP) — AEL-966. Extensive one-shot data
# migration, OFF by default (enable_orcamento_import). Reuses the API's user-assigned identity and the shared
# ACA env/ACR. Manual runs (Postman): POST /api/import starts the import in the background and returns 202 +
# a status URL; GET /api/import/status/{id} polls it; POST /api/import/preflight is synchronous (read-only).
module "container_app_orcamento_import" {
  count  = var.enable_orcamento_import ? 1 : 0
  source = "./modules/container_app"

  name                         = local.ca_orcamento_import_name
  resource_group_name          = module.resource_group.name
  container_app_environment_id = module.container_app_env.id

  image        = var.orcamento_import_image
  target_port  = 80 # Azure Functions isolated container listens on 80
  min_replicas = 1  # keep one replica warm during a run so the background import isn't killed by
  max_replicas = 1  # scale-to-zero; single writer (max 1) — never run concurrent imports. Set
                    # enable_orcamento_import=false to tear it down when the migration is done.

  identity_id      = module.identity.id
  acr_login_server = data.azurerm_container_registry.shared.login_server

  env_vars = {
    APPLICATIONINSIGHTS_CONNECTION_STRING = module.observability.application_insights_connection_string
    AZURE_CLIENT_ID                       = module.identity.client_id

    # Azure Functions host runtime.
    FUNCTIONS_WORKER_RUNTIME = "dotnet-isolated"

    # Host id fixo: as chaves (AuthorizationLevel.Function) são geradas pelo host e guardadas no blob
    # azure-webjobs-secrets/<hostId>/host.json (não há `az functionapp keys` para container em ACA).
    # Fixá-lo torna o caminho determinístico para recuperar a function key ao testar. Ver runbook.
    AzureFunctionsWebHost__hostId = "ulbra-one-import"

    # AzureWebJobsStorage (Functions host state) passwordless via the user-assigned identity. The identity has
    # Storage Blob + Queue + Table Data Contributor (modules/identity) so the host works fully passwordless.
    AzureWebJobsStorage__accountName = local.storage_name
    AzureWebJobsStorage__credential  = "managedidentity"
    AzureWebJobsStorage__clientId    = module.identity.client_id

    # Source Firestore (named database).
    Firestore__ProjectId  = var.firestore_project_id
    Firestore__DatabaseId = var.firestore_database_id

    # Blob for the migrated PDFs (payableFiles) — same account/container the app serves from, passwordless via
    # the identity (Storage Blob Data Contributor). No ObjectStorage__ConnectionString here, so the importer
    # authenticates by AccountUrl + managed identity. LegacyStorage is the source Firebase Storage bucket.
    ObjectStorage__AccountUrl = "https://${local.storage_name}.blob.core.windows.net"
    ObjectStorage__Bucket     = "attachments"
    LegacyStorage__Bucket     = "${var.firestore_project_id}.firebasestorage.app"
  }

  # DB connection string + GCP key from Key Vault (same identity the API uses).
  secret_env_vars = {
    "ConnectionStrings__Database" = module.key_vault.secret_versionless_ids["ConnectionStrings--Database"]
    "Firestore__CredentialsJson"  = module.key_vault.secret_versionless_ids["Firestore--CredentialsJson"]
  }

  tags = local.common_tags

  depends_on = [module.identity]
}

# ---------------------------------------------------------------------------
# Monitoring — alerts + dashboard
# ---------------------------------------------------------------------------
#
# Last module on purpose: it takes the container app and Postgres IDs as alert scopes,
# so it can only be built after them. See modules/monitoring/main.tf for why this is not
# folded into the observability module.
module "monitoring" {
  source = "./modules/monitoring"

  environment         = var.environment
  resource_group_name = module.resource_group.name
  location            = module.resource_group.location

  application_insights_id = module.observability.application_insights_id
  container_app_api_id    = module.container_app_api.id
  postgres_server_id      = module.postgres.id

  # min_replicas = 0 (dev) means "no replica" is the resting state, not an outage.
  enable_replica_alert = local.env.min_replicas > 0

  # Same value the container app is built from, so the memory alert tracks a resize on its own.
  api_memory = local.env.api_memory

  alert_sms_receivers   = var.alert_sms_receivers
  alert_email_receivers = var.alert_email_receivers

  tags = local.common_tags
}
