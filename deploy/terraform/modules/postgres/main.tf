# Azure Database for PostgreSQL Flexible Server reachable only from the virtual network
# (delegated subnet and private DNS zone), with one database per service. With
# high_availability_enabled the server keeps a synchronous standby in another zone and fails
# over to it automatically (zone-redundant HA); that needs a General Purpose or Memory
# Optimized SKU.

resource "random_password" "administrator" {
  length           = 32
  special          = true
  override_special = "-_.~"
}

resource "azurerm_private_dns_zone" "this" {
  name                = "${var.name}.private.postgres.database.azure.com"
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_private_dns_zone_virtual_network_link" "this" {
  name                  = "${var.name}-link"
  resource_group_name   = var.resource_group_name
  private_dns_zone_name = azurerm_private_dns_zone.this.name
  virtual_network_id    = var.virtual_network_id
  tags                  = var.tags
}

resource "azurerm_postgresql_flexible_server" "this" {
  name                          = var.name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  version                       = var.postgres_version
  sku_name                      = var.sku_name
  storage_mb                    = var.storage_mb
  auto_grow_enabled             = true
  backup_retention_days         = var.backup_retention_days
  geo_redundant_backup_enabled  = var.geo_redundant_backup_enabled
  delegated_subnet_id           = var.subnet_id
  private_dns_zone_id           = azurerm_private_dns_zone.this.id
  public_network_access_enabled = false
  administrator_login           = var.administrator_login
  administrator_password        = random_password.administrator.result
  zone                          = var.primary_zone

  dynamic "high_availability" {
    for_each = var.high_availability_enabled ? [1] : []

    content {
      mode                      = "ZoneRedundant"
      standby_availability_zone = var.standby_zone
    }
  }

  tags = var.tags

  lifecycle {
    # After a failover Azure swaps the primary and standby zones; do not fight it.
    ignore_changes = [zone, high_availability[0].standby_availability_zone]

    precondition {
      condition     = !var.high_availability_enabled || !startswith(var.sku_name, "B_")
      error_message = "High availability needs a General Purpose or Memory Optimized SKU, not Burstable."
    }

    precondition {
      condition     = !var.high_availability_enabled || var.primary_zone != var.standby_zone
      error_message = "The HA standby must be in a different zone from the primary."
    }
  }

  depends_on = [azurerm_private_dns_zone_virtual_network_link.this]
}

resource "azurerm_postgresql_flexible_server_database" "this" {
  for_each = toset(var.databases)

  name      = each.key
  server_id = azurerm_postgresql_flexible_server.this.id
  charset   = "UTF8"
  collation = "en_US.utf8"
}
