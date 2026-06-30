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

# RBAC-mode vault: the deployer needs a data-plane role ("Key Vault Secrets Officer") to
# read/write secrets. That role is granted OUTSIDE Terraform by infra/bootstrap/bootstrap.sh,
# so Terraform never has to bootstrap its own access — self-granting broke deploys whenever the
# GitHub OIDC identity changed (Terraform 403'd reading the secrets before its role applied).
resource "azurerm_key_vault_secret" "secrets" {
  # Secret *names* are not sensitive — only their values are. Declassify the keys
  # so they can drive for_each; values stay sensitive via the map lookup below.
  for_each     = nonsensitive(toset(keys(var.secrets)))
  name         = each.key
  value        = var.secrets[each.key]
  key_vault_id = azurerm_key_vault.this.id
}
