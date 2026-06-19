resource "azurerm_key_vault" "this" {
  name                       = var.name
  location                   = var.location
  resource_group_name        = var.resource_group_name
  tenant_id                  = var.tenant_id
  sku_name                   = "standard"
  rbac_authorization_enabled = true
  # Soft-delete is always on; set retention
  soft_delete_retention_days = 7
  purge_protection_enabled   = false
  tags                       = var.tags
}

# RBAC-mode vault: the deployer needs a data-plane role to write secrets.
# Owner/Contributor only grant management-plane access — not secret writes.
resource "azurerm_role_assignment" "deployer_secrets_officer" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = var.deployer_object_id
}

# RBAC propagation is eventually-consistent; give it time before the first write.
resource "time_sleep" "wait_role_propagation" {
  depends_on      = [azurerm_role_assignment.deployer_secrets_officer]
  create_duration = "60s"
}

resource "azurerm_key_vault_secret" "secrets" {
  # Secret *names* are not sensitive — only their values are. Declassify the keys
  # so they can drive for_each; values stay sensitive via the map lookup below.
  for_each     = nonsensitive(toset(keys(var.secrets)))
  name         = each.key
  value        = var.secrets[each.key]
  key_vault_id = azurerm_key_vault.this.id

  depends_on = [time_sleep.wait_role_propagation]
}
