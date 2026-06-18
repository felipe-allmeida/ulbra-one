resource "azurerm_key_vault" "this" {
  name                      = var.name
  location                  = var.location
  resource_group_name       = var.resource_group_name
  tenant_id                 = var.tenant_id
  sku_name                  = "standard"
  enable_rbac_authorization = true
  # Soft-delete is always on; set retention
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
  tags                       = var.tags
}

resource "azurerm_key_vault_secret" "secrets" {
  for_each     = var.secrets
  name         = each.key
  value        = each.value
  key_vault_id = azurerm_key_vault.this.id
}
