# Prod environment across three availability zones: a system pool and one autoscaled
# application pool per zone, PostgreSQL with a zone-redundant standby, a Premium Redis with
# replicas in every zone, a zone-redundant Premium registry and purge-protected Key Vault.

locals {
  name = "ecommerce-prod"
  tags = merge(var.tags, { environment = "prod" })
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
  address_space       = "10.30.0.0/16"

  aks_sku_tier                = "Standard"
  system_node_count           = 3
  app_node_vm_size            = "Standard_D4s_v5"
  app_node_min_count_per_zone = 1
  app_node_max_count_per_zone = 4

  container_registry_name = var.container_registry_name
  container_registry_sku  = "Premium"

  postgres_sku_name             = "GP_Standard_D2ds_v5"
  postgres_storage_mb           = 131072
  postgres_high_availability    = true
  postgres_geo_redundant_backup = true

  redis_sku_name = "Premium"
  redis_capacity = 1

  key_vault_name              = var.key_vault_name
  key_vault_purge_protection  = true
  key_vault_allowed_ip_ranges = var.key_vault_allowed_ip_ranges
  log_retention_days          = 90

  tags = local.tags
}
