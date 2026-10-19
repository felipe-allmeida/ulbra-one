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
  description = "Azure region for all resources — exceto o Redis, ver redis_location."
}

# East US, não eastus2 como o resto do stack. O pool da família Balanced do Azure Managed Redis
# está esgotado em East US 2: B0 e B1 são recusados na alocação com InsufficientCapacity em ~2 s,
# e não existe API de capacidade para consultar antes (a própria Microsoft confirma). Medido em
# 2026-08-29: East US e North Central US aceitam B0; East US 2 só aceita MemoryOptimized_M10, que
# custa 13× o preço. O preço dos 9 ms de round-trip entre as regiões é cobrado num lugar só —
# ReadPresenceAsync é a única operação síncrona do backplane (o resto é FireAndForget) e tem um
# único caller, o endpoint GetPresence. Quando houver capacidade em East US 2, isto volta para
# var.location: recriar o recurso não perde nada, o backplane não guarda dado durável.
variable "redis_location" {
  type        = string
  default     = "eastus"
  description = "Azure region for the realtime backplane (diverge de location por capacidade)."
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

# web_custom_domain, api_min_replicas, web_min_replicas e postgres_sku_name NÃO são variáveis:
# são derivados de var.environment em locals.per_env (fim deste arquivo). Ver o comentário lá.

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
  default     = "0 20 * * *"
  description = "Cron (UTC) for the remittance-sweep job. ACA Jobs schedule in UTC; default 20:00 UTC = 17:00 BRT (horário de corte diário; Brasília é UTC-3, sem DST). Runs every day."
}

variable "enable_requisition_sla_job" {
  type        = bool
  default     = true
  description = "Provision the requisition SLA escalation Container App Job (AEL-872). Set false to disable in an environment."
}

variable "requisition_sla_cron" {
  type        = string
  default     = "0 * * * *"
  description = "Cron (UTC) for the requisition SLA escalation sweep. ACA Jobs schedule in UTC; default = hourly (SLA is measured in business hours, so an hourly check is granular enough)."
}

variable "web_max_replicas" {
  type        = number
  default     = 2
  description = "Maximum replica count for the web container app."
}

# NÃO existe variable "jwt_secret" — o valor é gerado por random_password.jwt em main.tf e
# gravado no Key Vault. Ver o comentário do recurso para o porquê. Se algum .tfvars ainda
# definir jwt_secret, o Terraform emite apenas um aviso de "undeclared variable" e ignora.

variable "ai_api_key" {
  type        = string
  sensitive   = true
  description = "Anthropic API key for the payable AI classifier. Stored in Key Vault as 'Ai--ApiKey'. Supply via -var / TF_VAR_ai_api_key; never commit the value."
}

# ---------------------------------------------------------------------------
# Alertas — destinatários
# ---------------------------------------------------------------------------
#
# Telefone e e-mail são dado pessoal e não entram no repositório. Chegam por
# TF_VAR_alert_sms_receivers / TF_VAR_alert_email_receivers (JSON em GitHub Secret),
# pelo mesmo caminho que ai_api_key e postgres_admin_login já usam. O default vazio
# mantém o apply funcionando sem os secrets configurados: os alertas são criados e
# aparecem no portal, apenas não notificam ninguém.

variable "alert_sms_receivers" {
  type = list(object({
    name         = string
    country_code = string
    phone_number = string
  }))
  default     = []
  sensitive   = true
  description = "Destinatários de SMS dos alertas. Ex.: [{\"name\":\"oncall\",\"country_code\":\"55\",\"phone_number\":\"51900000001\"}]. O phone_number vai sem DDI, sem espaço e sem pontuação."
}

variable "alert_email_receivers" {
  type = list(object({
    name  = string
    email = string
  }))
  default     = []
  sensitive   = true
  description = "Destinatários de e-mail dos alertas. Mesmo formato e mesmo caminho de entrega dos SMS."
}

variable "google_allowed_domains" {
  type        = list(string)
  default     = ["ulbra.br", "aelbra.com.br"]
  description = <<-EOT
    Domínios de e-mail autorizados a fazer sign-in com Google. Vira Authentication__Google__AllowedDomains__N.

    Precisa vir daqui — e não de appsettings — porque essas listas só existem em
    appsettings.Development.json, que não carrega fora de Development. Sem isso, prod e stage sobem
    com a lista VAZIA, e GoogleAuthOptions.IsEmailAllowed trata vazio como "permite qualquer conta
    Google verificada". O default cobre também a pipeline, que aplica sem -var-file.
  EOT
}

variable "google_bootstrap_admins" {
  type        = list(string)
  default     = ["redacted-08@example.invalid"]
  description = <<-EOT
    E-mails que entram como Admin no primeiro sign-in. Vira Authentication__Google__BootstrapAdmins__N.

    IsBootstrapAdmin é avaliado ANTES do filtro de domínio, então estas contas entram mesmo fora de
    google_allowed_domains. Mantenha a lista mínima. Sem ao menos uma entrada não há como promover
    ninguém: todo usuário novo nasce com a role User, que não tem permissão alguma.
  EOT
}

variable "tags" {
  type        = map(string)
  default     = {}
  description = "Common tags applied to all resources."
}

# ---------------------------------------------------------------------------
# Orçamento importer (Firestore → One) — off by default; a one-shot migration tool
# ---------------------------------------------------------------------------

variable "enable_orcamento_import" {
  type        = bool
  default     = false
  description = "Deploy the Orçamento importer Container App (Azure Functions, HTTP trigger). Off by default — it is a one-shot migration tool, enabled only when a run is planned."
}

variable "orcamento_import_image" {
  type        = string
  default     = ""
  description = "Container image for the importer (crulbraone.azurecr.io/orcamento-import:<tag>). Required only when enable_orcamento_import = true."
}

variable "firestore_project_id" {
  type        = string
  default     = "spherical-cell-g07pf"
  description = "GCP project id of the legacy orçamento Firestore (source)."
}

variable "firestore_database_id" {
  type        = string
  default     = "ai-studio-9c0ba35e-15c2-4fa9-96ad-eb738fcf5bb5"
  description = "Firestore NAMED database id of the source (not '(default)')."
}

variable "firestore_credentials_json" {
  type        = string
  default     = ""
  sensitive   = true
  description = <<-EOT
    GCP service-account key JSON with read access to the source Firestore. Stored as the Key Vault
    secret Firestore--CredentialsJson and injected into the importer as Firestore__CredentialsJson.
    Leave empty unless enable_orcamento_import = true. A mais segura alternativa é Workload Identity
    Federation (Azure → GCP), que dispensaria guardar a chave — fora do escopo deste scaffold.
  EOT
}

# ---------------------------------------------------------------------------
# CAF-computed names (locals in main.tf reference var.environment)
# ---------------------------------------------------------------------------
locals {
  # -------------------------------------------------------------------------
  # Configuração por ambiente — fonte única de verdade
  # -------------------------------------------------------------------------
  #
  # Estes valores NÃO são variáveis de propósito. A pipeline aplica com apenas cinco -var
  # (environment, google_client_id, api_image, web_image e nada mais) e sem -var-file — os
  # *.tfvars são gitignored, então a CI não consegue lê-los. Enquanto SKU e réplicas viviam em
  # tfvars, um apply pela pipeline revertia silenciosamente o que fora aplicado localmente:
  # o prod voltaria para B1ms e min_replicas=0.
  #
  # Derivando de var.environment aqui, CI e apply local concordam por construção. Escalar um
  # ambiente passa a ser editar este mapa — mudança versionada e revisável, não um arquivo local.
  per_env = {
    dev = {
      postgres_sku = "B_Standard_B1ms"
      # Sem réplica no backplane: em dev a queda do Redis custa presença, não dinheiro. É a única
      # diferença de Redis entre ambientes — o SKU é o menor (Balanced_B0) nos três.
      redis_high_availability = false
      min_replicas            = 0 # playground; o cold start de ~66 s é aceitável
      api_cpu                 = "0.25"
      api_memory              = "0.5Gi"
      web_custom_domain       = "dev.one.ulbra.ai"
      aspnetcore_env          = "Development"
      vite_env                = "development"
      # null: um custom_scale_rule SUBSTITUI a regra HTTP default do Container Apps, e regra de CPU
      # não escala a partir de zero (não há container rodando para medir CPU). Com min_replicas=0
      # aqui, uma regra de CPU incondicional deixaria o app preso — sem HTTP rule para acordar do
      # zero e sem CPU rule capaz de fazer isso também. dev mantém a HTTP rule default de propósito.
      api_cpu_scale_threshold = null
    }
    stage = {
      postgres_sku            = "B_Standard_B1ms"
      redis_high_availability = true
      min_replicas            = 1 # o negócio abre o link; não pode esperar o container subir
      api_cpu                 = "0.25"
      api_memory              = "0.5Gi"
      web_custom_domain       = ""
      aspnetcore_env          = "Staging"
      vite_env                = "staging"
      # min_replicas=1 aqui: sempre há um container rodando para medir CPU, então a regra é segura.
      # Ver o comentário em dev para o porquê de não valer lá.
      api_cpu_scale_threshold = 70
    }
    prod = {
      postgres_sku            = "B_Standard_B2s" # 429 conexões contra as 35 do B1ms — ver o spec
      redis_high_availability = true
      min_replicas            = 1

      # Dimensionamento da API. Medido em 14 dias de produção com 0.25 vCPU / 0.5Gi:
      #
      #   CPU  — pico horário: p50 0,022 · p90 0,275 · p99 0,373 · máx 0,393 core.
      #          43 das 336 horas (13%) tiveram pico ACIMA do limite de 0,25. É bimodal:
      #          ocioso a ~9% do limite quase sempre e, quando trabalha, vai direto a
      #          ~0,39 — ou seja, é throttled exatamente quando precisa. Daí o p50 de 12 ms
      #          com p95 de ~1 s nos GETs, e o p95 de 3,8 s até no /api/ping.
      #   RAM  — média 394 MB, pico 464 MB contra o limite de 512 MB (86%).
      #
      # 0,5 vCPU cobre o pico medido com ~27% de folga. A memória não é escolha: no perfil
      # Consumption o Container Apps trava 2 GiB por vCPU, então 0.5 vCPU implica 1 GiB.
      #
      # A memória NÃO é problema de dimensionamento — cresce ~15 MB/dia de forma linear e
      # independente de tráfego, e só volta ao normal quando o processo recicla. 1 GiB leva o
      # tempo até 85% do limite de ~10 para ~39 dias; é fôlego, não solução. A hipótese está
      # na retenção por operação do polling do outbox (ver #228). O app web fica em 0.25/0.5Gi:
      # usa 70 MB de 512 e não cresce, o que reforça que o crescimento é da API.
      api_cpu    = "0.5"
      api_memory = "1Gi"
      # Hoje isto só alimenta CORS (AllowedOrigins da API + CORS do blob) — nenhum recurso faz o
      # binding do domínio ainda. Declarar antes do DNS existir é inofensivo: libera uma origem
      # que ninguém ainda resolve. O binding em si (azurerm_container_app_custom_domain +
      # certificado gerenciado) ainda não foi implementado; quando for, aí sim exigirá o
      # CNAME one + TXT asuid.one na zona ulbra.ai antes do apply.
      web_custom_domain = "one.ulbra.ai"
      # Governa o guard de /auth/dev-token no backend e o login rápido por cargo na SPA.
      # Sem "stage" no mapa, o ternário antigo classificava tudo que não fosse prod como
      # Development — o que ligaria o dev-token no stage.
      aspnetcore_env = "Production"
      vite_env       = "production"
      # min_replicas=1 aqui, mesmo raciocínio do stage — ver o comentário em dev.
      api_cpu_scale_threshold = 70
    }
  }
  env = local.per_env[var.environment]

  rg_name     = "rg-ulbraone-${var.environment}"
  cae_name    = "cae-ulbraone-${var.environment}"
  ca_api_name = "ca-ulbraone-api-${var.environment}"
  ca_web_name = "ca-ulbraone-web-${var.environment}"
  # Importador Orçamento (Firestore → One) — Azure Functions containerizado na ACA (HTTP trigger).
  ca_orcamento_import_name = "ca-ulbraone-import-${var.environment}"

  # Container App Job (scheduled) — daily CNAB remittance sweep.
  # Azure Container App Job names are capped at 32 chars — keep this short ("remit-sweep", not "remittance-sweep").
  caj_remittance_sweep_name = "caj-ulbraone-remit-sweep-${var.environment}"
  # Requisition SLA escalation sweep (AEL-872). Also <=32 chars ("req-sla", not "requisition-sla").
  caj_requisition_sla_name = "caj-ulbraone-req-sla-${var.environment}"
  psql_name                = "psql-ulbraone-${var.environment}"
  sb_name                  = "sb-ulbraone-${var.environment}"
  redis_name               = "redis-ulbraone-${var.environment}"
  storage_name             = "stulbraone${var.environment}" # no dashes, <=24 chars
  kv_name                  = "kv-ulbraone-${var.environment}"
  identity_name            = "id-ulbraone-api-${var.environment}"
  log_name                 = "log-ulbraone-${var.environment}"
  appi_name                = "appi-ulbraone-${var.environment}"

  # ACR is shared across environments — created by bootstrap (Task C3)
  acr_name    = "crulbraone"
  acr_rg_name = "rg-ulbraone-tfstate"

  # Browser origins allowed by API CORS + blob CORS: the default web FQDN plus any custom domain.
  web_origins = compact(concat(
    ["https://${local.ca_web_name}.${module.container_app_env.default_domain}"],
    local.env.web_custom_domain != "" ? ["https://${local.env.web_custom_domain}"] : [],
  ))
  # Indexed env vars (AllowedOrigins__0, __1, …) bind to the app's AllowedOrigins string[] config.
  cors_env_vars = { for i, o in local.web_origins : "AllowedOrigins__${i}" => o }

  # Guarda do sign-in com Google, no mesmo formato indexado (o binder do ASP.NET Core monta o
  # string[] a partir de __0, __1, …). Ver as descrições das variáveis para o porquê de estarem aqui.
  google_env_vars = merge(
    { for i, d in var.google_allowed_domains : "Authentication__Google__AllowedDomains__${i}" => d },
    { for i, a in var.google_bootstrap_admins : "Authentication__Google__BootstrapAdmins__${i}" => a },
  )

  common_tags = merge(var.tags, {
    environment = var.environment
    managed_by  = "terraform"
    project     = "ulbra-one"
  })
}
