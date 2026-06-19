# Dev environment — small SKUs, scale-to-zero
environment          = "dev"
location             = "eastus2"
postgres_admin_login = "ulbraadmin"
google_client_id     = "867854924898-uvv5b8h238cb2ctg3te66vri829dckla.apps.googleusercontent.com" # public OAuth client id (frontend + backend audience)

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

# Postgres vertical scaling — override to a GP SKU for more CPU/RAM
# postgres_sku_name = "GP_Standard_D2s_v3"
postgres_sku_name = "B_Standard_B1ms"

# jwt_secret must be supplied via -var or TF_VAR_jwt_secret (never commit the value)
