resource "azurerm_postgresql_flexible_server" "this" {
  name                   = var.name
  resource_group_name    = var.resource_group_name
  location               = var.location
  version                = "16"
  administrator_login    = var.administrator_login
  administrator_password = var.administrator_password
  sku_name               = var.sku_name
  storage_mb             = 32768
  # B-series SKU does not support HA; omitting high_availability block = Disabled.
  # No delegated_subnet_id = public network access enabled; firewall rules control ingress.
  tags = var.tags
}

resource "azurerm_postgresql_flexible_server_database" "ulbra_one" {
  name      = "ulbra_one"
  server_id = azurerm_postgresql_flexible_server.this.id
  collation = "en_US.utf8"
  charset   = "UTF8"
}

# Allow all Azure-origin IPs (0.0.0.0 start/end = Azure services rule)
resource "azurerm_postgresql_flexible_server_firewall_rule" "allow_azure" {
  name             = "AllowAllAzureServices"
  server_id        = azurerm_postgresql_flexible_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}
