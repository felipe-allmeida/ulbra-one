resource "azurerm_storage_account" "this" {
  name                     = var.name
  resource_group_name      = var.resource_group_name
  location                 = var.location
  account_tier             = "Standard"
  account_replication_type = "LRS"
  # Enforce HTTPS and disable public blob access
  https_traffic_only_enabled      = true
  allow_nested_items_to_be_public = false
  tags                            = var.tags

  # The fiscal-document upload PUTs files directly from the browser to the blob service via SAS,
  # so the storage account must allow the web origin via CORS. No rule when no origins are provided.
  dynamic "blob_properties" {
    for_each = length(var.cors_allowed_origins) > 0 ? [1] : []
    content {
      cors_rule {
        allowed_origins    = var.cors_allowed_origins
        allowed_methods    = ["GET", "PUT", "OPTIONS", "HEAD"]
        allowed_headers    = ["*"]
        exposed_headers    = ["*"]
        max_age_in_seconds = 3600
      }
    }
  }
}

resource "azurerm_storage_container" "attachments" {
  name                  = "attachments"
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"
}

# ASP.NET Core Data Protection key ring. Separate from "attachments" because that one is reachable
# from the browser via user-delegation SAS and carries the account's CORS rule. Terraform owns it
# because the ring is read lazily — a missing container would surface as a failed login, not a
# startup error.
resource "azurerm_storage_container" "dataprotection" {
  name                  = "dataprotection"
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"
}
