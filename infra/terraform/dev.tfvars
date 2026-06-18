# Dev environment — small SKUs, scale-to-zero
environment          = "dev"
location             = "eastus2"
postgres_admin_login = "ulbraadmin"
google_client_id     = "" # set in pipeline or local override

# Images are updated by CI on each push to main
api_image = "crulbraone.azurecr.io/api:latest"
web_image = "crulbraone.azurecr.io/web:latest"

# Scale-to-zero for dev to minimise cost
api_min_replicas = 0
api_max_replicas = 2
web_min_replicas = 0
web_max_replicas = 2

tags = {
  environment = "dev"
  team        = "ulbra-one"
}

# jwt_secret must be supplied via -var or TF_VAR_jwt_secret (never commit the value)
