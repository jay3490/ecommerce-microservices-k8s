# Dev environment: the same platform as prod at the smallest sizes. A single zone for the
# node pool, Burstable PostgreSQL without HA, Standard Redis and a Basic registry.

locals {
  name = "ecommerce-dev"
  tags = merge(var.tags, { environment = "dev" })
}

resource "azurerm_resource_group" "this" {
  name     = "rg-${local.name}"
  location = var.location
  tags     = local.tags
}

module "platform" {
  source = "../../modules/platform"

  name                = local.name
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  zones               = var.zones
  address_space       = "10.20.0.0/16"

  aks_sku_tier                = "Free"
  system_node_count           = 1
  app_node_vm_size            = "Standard_D2s_v5"
  app_node_min_count_per_zone = 1
  app_node_max_count_per_zone = 2

  container_registry_name = var.container_registry_name
  container_registry_sku  = "Basic"

  postgres_sku_name          = "B_Standard_B1ms"
  postgres_storage_mb        = 32768
  postgres_high_availability = false

  redis_sku_name = "Standard"
  redis_capacity = 0

  key_vault_name             = var.key_vault_name
  key_vault_purge_protection = false
  log_retention_days         = 30

  tags = local.tags
}
