# Remote state is stored in the shared bootstrap storage account.
#
# The `key` is set per-environment at `terraform init` time via:
#   terraform init -backend-config="key=dev.tfstate"
#   terraform init -backend-config="key=prod.tfstate"
#
# The storage account, resource group, and container below are created by the
# bootstrap script (Task C3) before any `terraform apply` is run.

terraform {
  backend "azurerm" {
    resource_group_name  = "rg-ulbraone-tfstate"
    storage_account_name = "stulbraonetfstate"
    container_name       = "tfstate"
    # key is supplied via -backend-config at init; do NOT hardcode here.
  }
}
