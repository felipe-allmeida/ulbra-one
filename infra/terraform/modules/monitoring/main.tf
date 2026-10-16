# ---------------------------------------------------------------------------
# Monitoring — action group, alert rules and the environment dashboard
# ---------------------------------------------------------------------------
#
# Separate from the `observability` module on purpose. `observability` provisions the
# COLLECTORS (Log Analytics + Application Insights) and has to exist before the container
# apps, because they read its connection string. This module CONSUMES what was collected
# and needs the container app and Postgres resource IDs as alert scopes — so it is
# instantiated last. Merging the two would create a dependency cycle.

locals {
  # Alerts group under one action group per environment. Short name is capped at 12 chars by Azure.
  action_group_short_name = substr("one-${var.environment}", 0, 12)

  # Metric alerts want bytes; the container_app module declares memory as "<n>Gi".
  api_memory_bytes          = tonumber(trimsuffix(var.api_memory, "Gi")) * 1024 * 1024 * 1024
  memory_pressure_threshold = local.api_memory_bytes * 0.85
}

resource "azurerm_monitor_action_group" "this" {
  name                = "ag-ulbraone-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = "global"
  short_name          = local.action_group_short_name
  tags                = var.tags

  dynamic "sms_receiver" {
    for_each = { for r in var.alert_sms_receivers : r.name => r }
    content {
      name         = sms_receiver.value.name
      country_code = sms_receiver.value.country_code
      phone_number = sms_receiver.value.phone_number
    }
  }

  dynamic "email_receiver" {
    for_each = { for r in var.alert_email_receivers : r.name => r }
    content {
      name                    = email_receiver.value.name
      email_address           = email_receiver.value.email
      use_common_alert_schema = true
    }
  }
}

# ---------------------------------------------------------------------------
# Log alerts (Application Insights)
# ---------------------------------------------------------------------------

# Sev 1 — the app is returning server errors. Any 5xx is a bug by definition: every expected
# failure in this codebase leaves through ErrorResults as a 4xx.
resource "azurerm_monitor_scheduled_query_rules_alert_v2" "http_5xx" {
  name                = "alert-${var.environment}-api-5xx"
  resource_group_name = var.resource_group_name
  location            = var.location
  scopes              = [var.application_insights_id]
  severity            = 1
  description         = "A API respondeu 5xx. Todo erro esperado sai como 4xx via ErrorResults, então 5xx é sempre bug."
  tags                = var.tags

  evaluation_frequency    = "PT5M"
  window_duration         = "PT15M"
  auto_mitigation_enabled = true

  criteria {
    query                   = <<-KQL
      requests
      | where toint(resultCode) >= 500
      | summarize Erros = count()
    KQL
    time_aggregation_method = "Total"
    metric_measure_column   = "Erros"
    threshold               = var.http_5xx_threshold
    operator                = "GreaterThanOrEqual"

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.this.id]
  }
}

# Sev 2 — exception spike. Unhandled exceptions that never reach a 5xx still matter: the
# seed failure in issue #225 is exactly this shape (logged, swallowed, invisible for weeks).
resource "azurerm_monitor_scheduled_query_rules_alert_v2" "exception_spike" {
  name                = "alert-${var.environment}-api-excecoes"
  resource_group_name = var.resource_group_name
  location            = var.location
  scopes              = [var.application_insights_id]
  severity            = 2
  description         = "Pico de exceções na API. Cobre também as exceções engolidas por log-and-continue, que não viram 5xx."
  tags                = var.tags

  evaluation_frequency    = "PT5M"
  window_duration         = "PT15M"
  auto_mitigation_enabled = true

  criteria {
    query                   = <<-KQL
      exceptions
      | summarize Excecoes = count()
    KQL
    time_aggregation_method = "Total"
    metric_measure_column   = "Excecoes"
    threshold               = var.exception_threshold
    operator                = "GreaterThanOrEqual"

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.this.id]
  }
}

# Sev 3 — latency. OPTIONS preflights are excluded: they answer 204 in ~0 ms and would drag
# the percentile down far enough to mask a real regression (they are ~47% of all requests today).
# The `Amostras > 40` guard keeps a quiet night from paging anyone on a p95 computed from a
# handful of requests — on the quietest days production serves under 30 requests in a whole day.
#
# DO NOT raise number_of_evaluation_periods above 1 here. Azure rejects the rule at create time
# ("Number of evaluation periods must be 1 for queries that do not project the 'timestamp' column
# of type 'datetime'", 400) unless the query bins by time and projects that timestamp — and this
# one deliberately collapses the window to a single percentile. The debounce that the multi-period
# setting was there to provide comes from the 30-minute window instead: half an hour of degraded
# p95 is a regression, not a blip. Terraform cannot catch this — validate and plan both pass, and
# only the apply fails.
resource "azurerm_monitor_scheduled_query_rules_alert_v2" "latency_p95" {
  name                = "alert-${var.environment}-api-latencia"
  resource_group_name = var.resource_group_name
  location            = var.location
  scopes              = [var.application_insights_id]
  severity            = 3
  description         = "p95 de latência degradado. Ignora preflights CORS e janelas com pouco tráfego."
  tags                = var.tags

  evaluation_frequency    = "PT15M"
  window_duration         = "PT30M"
  auto_mitigation_enabled = true

  criteria {
    query                   = <<-KQL
      requests
      | where name !startswith 'OPTIONS'
      | summarize P95 = percentile(duration, 95), Amostras = count()
      | where Amostras > 40
      | project P95
    KQL
    time_aggregation_method = "Maximum"
    metric_measure_column   = "P95"
    threshold               = var.latency_p95_threshold_ms
    operator                = "GreaterThan"

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.this.id]
  }
}

# Sev 2 — dependency failures (Postgres, Service Bus, Blob).
# Two known-benign patterns are filtered, both observed in production and both once-per-boot:
#   • Blob 409 — BlobContainerInitializer's CreateIfNotExists on the existing 'attachments' container.
#   • Service Bus 404 — MassTransit probing a topic/subscription before declaring it.
# Without these filters the rule fires on every single deploy.
resource "azurerm_monitor_scheduled_query_rules_alert_v2" "dependency_failures" {
  name                = "alert-${var.environment}-dependencias"
  resource_group_name = var.resource_group_name
  location            = var.location
  scopes              = [var.application_insights_id]
  severity            = 2
  description         = "Falhas em dependências externas (Postgres, Service Bus, Blob), já descontados os 409/404 benignos de bootstrap."
  tags                = var.tags

  evaluation_frequency    = "PT5M"
  window_duration         = "PT15M"
  auto_mitigation_enabled = true

  criteria {
    query                   = <<-KQL
      dependencies
      | where success == false
      | where not(type == 'Azure blob' and resultCode == '409')
      | where not(type == 'HTTP' and target has 'servicebus.windows.net' and resultCode == '404')
      | summarize Falhas = count()
    KQL
    time_aggregation_method = "Total"
    metric_measure_column   = "Falhas"
    threshold               = var.dependency_failure_threshold
    operator                = "GreaterThanOrEqual"

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.this.id]
  }
}

# ---------------------------------------------------------------------------
# Metric alerts (Container App / Postgres)
# ---------------------------------------------------------------------------

# Sev 2 — memory pressure. Fires at 85% of the container's own limit, read from the same
# local.env.api_memory the container app is built from — so it tracks a resize instead of
# needing a second edit. At prod's 1Gi that is ~870 MB.
#
# Worth knowing when this fires: production memory grows ~15 MB/day, linearly and independently
# of traffic, resetting only when the process recycles (#232). So this alert is a clock, not a
# load signal — if it opens, the question is what is being retained, not whether traffic spiked.
resource "azurerm_monitor_metric_alert" "api_memory" {
  name                = "alert-${var.environment}-api-memoria"
  resource_group_name = var.resource_group_name
  scopes              = [var.container_app_api_id]
  severity            = 2
  description         = "Memória da API acima de 85% do limite do container. Risco de OOM kill."
  frequency           = "PT5M"
  window_size         = "PT15M"
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.App/containerapps"
    metric_name      = "WorkingSetBytes"
    aggregation      = "Maximum"
    operator         = "GreaterThan"
    threshold        = local.memory_pressure_threshold
  }

  action {
    action_group_id = azurerm_monitor_action_group.this.id
  }
}

# Sev 1 — no healthy replica. Guarded by a flag because dev runs min_replicas = 0 on purpose:
# there, zero replicas is the resting state, not an outage.
resource "azurerm_monitor_metric_alert" "api_no_replicas" {
  count = var.enable_replica_alert ? 1 : 0

  name                = "alert-${var.environment}-api-sem-replica"
  resource_group_name = var.resource_group_name
  scopes              = [var.container_app_api_id]
  severity            = 1
  description         = "A API ficou sem nenhuma réplica rodando."
  frequency           = "PT5M"
  window_size         = "PT15M"
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.App/containerapps"
    metric_name      = "Replicas"
    aggregation      = "Maximum"
    operator         = "LessThan"
    threshold        = 1
  }

  action {
    action_group_id = azurerm_monitor_action_group.this.id
  }
}

# Sev 2 — Postgres CPU. The server is a burstable B-series, so sustained high CPU means the
# burst credits are draining and everything downstream is about to get slow.
resource "azurerm_monitor_metric_alert" "postgres_cpu" {
  name                = "alert-${var.environment}-postgres-cpu"
  resource_group_name = var.resource_group_name
  scopes              = [var.postgres_server_id]
  severity            = 2
  description         = "CPU do Postgres acima de 80%. Em SKU burstable, isso drena os créditos de burst."
  frequency           = "PT5M"
  window_size         = "PT15M"
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.DBforPostgreSQL/flexibleServers"
    metric_name      = "cpu_percent"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 80
  }

  action {
    action_group_id = azurerm_monitor_action_group.this.id
  }
}

# Sev 2 — Postgres storage. Running out of disk is an outage with no graceful degradation.
resource "azurerm_monitor_metric_alert" "postgres_storage" {
  name                = "alert-${var.environment}-postgres-disco"
  resource_group_name = var.resource_group_name
  scopes              = [var.postgres_server_id]
  severity            = 2
  description         = "Disco do Postgres acima de 85%."
  frequency           = "PT15M"
  window_size         = "PT1H"
  tags                = var.tags

  criteria {
    metric_namespace = "Microsoft.DBforPostgreSQL/flexibleServers"
    metric_name      = "storage_percent"
    aggregation      = "Average"
    operator         = "GreaterThan"
    threshold        = 85
  }

  action {
    action_group_id = azurerm_monitor_action_group.this.id
  }
}

# ---------------------------------------------------------------------------
# Dashboard
# ---------------------------------------------------------------------------
#
# An Azure Monitor Workbook, not a Portal dashboard: workbooks take arbitrary KQL, carry their
# own time-range parameter, and are diffable as JSON in review. The name is a UUID because
# Azure keys workbooks by GUID; uuidv5 over the environment keeps it stable across applies.
resource "azurerm_application_insights_workbook" "dashboard" {
  name                = uuidv5("url", "https://one.ulbra.ai/workbooks/${var.environment}")
  resource_group_name = var.resource_group_name
  location            = var.location
  display_name        = "Ulbra.One — ${var.environment}"
  source_id           = lower(var.application_insights_id)
  category            = "workbook"
  tags                = var.tags

  data_json = templatefile("${path.module}/workbook.json.tftpl", {
    env_name  = var.environment
    appi_id   = var.application_insights_id
    ca_api_id = var.container_app_api_id
  })
}
