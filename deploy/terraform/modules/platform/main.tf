# Everything one environment needs on Azure: a virtual network, Log Analytics, a zonal AKS
# cluster, a container registry it can pull from, PostgreSQL Flexible Server with one
# database per service, Azure Cache for Redis, and a Key Vault with the services' connection
# strings. The environments (../../environments/dev and prod) call this module with their
# own sizes and zone settings.
#
# Not provisioned here: RabbitMQ (Azure has no managed RabbitMQ; run it in the cluster, for
# example with the RabbitMQ cluster operator) and MongoDB for the reviews service (Azure
# Cosmos DB for MongoDB or MongoDB Atlas would be the managed options).

locals {
  # Key Vault secret name => the key in the ecommerce-secrets Secret it becomes.
  database_secrets = { for database in var.databases : "${database}-db" => database }
}

resource "azurerm_virtual_network" "this" {
  name                = "vnet-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  address_space       = [var.address_space]
  tags                = var.tags
}

resource "azurerm_subnet" "aks" {
  name                 = "snet-aks"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [cidrsubnet(var.address_space, 4, 0)]
}

resource "azurerm_subnet" "postgres" {
  name                 = "snet-postgres"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [cidrsubnet(var.address_space, 8, 16)]

  delegation {
    name = "postgres"

    service_delegation {
      name    = "Microsoft.DBforPostgreSQL/flexibleServers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}

resource "azurerm_subnet" "private_endpoints" {
  name                 = "snet-private-endpoints"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [cidrsubnet(var.address_space, 8, 17)]
}

module "log_analytics" {
  source = "../log-analytics"

  name                = "log-${var.name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  retention_in_days   = var.log_retention_days
  tags                = var.tags
}

module "aks" {
  source = "../aks"

  name                        = "aks-${var.name}"
  resource_group_name         = var.resource_group_name
  location                    = var.location
  kubernetes_version          = var.kubernetes_version
  sku_tier                    = var.aks_sku_tier
  zones                       = var.zones
  subnet_id                   = azurerm_subnet.aks.id
  log_analytics_workspace_id  = module.log_analytics.id
  system_node_count           = var.system_node_count
  app_node_vm_size            = var.app_node_vm_size
  app_node_min_count_per_zone = var.app_node_min_count_per_zone
  app_node_max_count_per_zone = var.app_node_max_count_per_zone
  tags                        = var.tags
}

module "acr" {
  source = "../acr"

  name                    = var.container_registry_name
  resource_group_name     = var.resource_group_name
  location                = var.location
  sku                     = var.container_registry_sku
  zone_redundancy_enabled = length(var.zones) > 1
  tags                    = var.tags
}

# The nodes pull images from the registry without image pull secrets.
resource "azurerm_role_assignment" "acr_pull" {
  scope                            = module.acr.id
  role_definition_name             = "AcrPull"
  principal_id                     = module.aks.kubelet_identity_object_id
  skip_service_principal_aad_check = true
}

module "postgres" {
  source = "../postgres"

  name                         = "psql-${var.name}"
  resource_group_name          = var.resource_group_name
  location                     = var.location
  virtual_network_id           = azurerm_virtual_network.this.id
  subnet_id                    = azurerm_subnet.postgres.id
  sku_name                     = var.postgres_sku_name
  storage_mb                   = var.postgres_storage_mb
  high_availability_enabled    = var.postgres_high_availability
  geo_redundant_backup_enabled = var.postgres_geo_redundant_backup
  primary_zone                 = length(var.zones) > 0 ? var.zones[0] : null
  standby_zone                 = length(var.zones) > 1 ? var.zones[1] : null
  databases                    = var.databases
  tags                         = var.tags
}

module "redis" {
  source = "../redis"

  name                = "redis-${var.name}"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku_name            = var.redis_sku_name
  capacity            = var.redis_capacity
  zones               = var.zones
  virtual_network_id  = azurerm_virtual_network.this.id
  subnet_id           = azurerm_subnet.private_endpoints.id
  tags                = var.tags
}

module "key_vault" {
  source = "../key-vault"

  name                       = var.key_vault_name
  resource_group_name        = var.resource_group_name
  location                   = var.location
  purge_protection_enabled   = var.key_vault_purge_protection
  soft_delete_retention_days = var.key_vault_purge_protection ? 90 : 7
  allowed_ip_ranges          = var.key_vault_allowed_ip_ranges
  tags                       = var.tags

  readers = {
    aks-secrets-provider = module.aks.key_vault_secrets_provider_object_id
  }

  # Secret names match the keys of the ecommerce-secrets Secret in deploy/k8s.
  secrets = merge(
    { for secret, database in local.database_secrets : secret => module.postgres.connection_strings[database] },
    {
      "redis"             = module.redis.connection_string
      "postgres-password" = module.postgres.administrator_password
    },
  )
}
