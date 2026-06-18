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
}

resource "azurerm_storage_container" "attachments" {
  name                  = "attachments"
  storage_account_id    = azurerm_storage_account.this.id
  container_access_type = "private"
}
