# Prod environment — same small SKUs for now; at least 1 replica for warm start
environment          = "prod"
location             = "eastus2"
postgres_admin_login = "ulbraadmin"
google_client_id     = "" # set in pipeline or local override

# Images are pinned to digest / release tag in prod CI
api_image = "crulbraone.azurecr.io/api:latest"
web_image = "crulbraone.azurecr.io/web:latest"

# At least 1 replica in prod to avoid cold-start on first request
api_min_replicas = 1
api_max_replicas = 2
web_min_replicas = 0
web_max_replicas = 2

tags = {
  environment = "prod"
  team        = "ulbra-one"
}

# jwt_secret must be supplied via -var or TF_VAR_jwt_secret (never commit the value)
