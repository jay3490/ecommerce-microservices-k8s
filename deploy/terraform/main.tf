# Example root configuration: a resource group and an AKS cluster from ./modules/aks.
# It is a starting point for running the services on Azure, not a description of an
# existing environment. Review sizes, versions and networking before applying.

locals {
  name = "ecommerce-${var.environment}"
}

resource "azurerm_resource_group" "this" {
  name     = "rg-${local.name}"
  location = var.location
  tags     = var.tags
}

module "aks" {
  source = "./modules/aks"

  name                    = "aks-${local.name}"
  resource_group_name     = azurerm_resource_group.this.name
  location                = azurerm_resource_group.this.location
  container_registry_name = var.container_registry_name
  sku_tier                = var.environment == "prod" ? "Standard" : "Free"
  app_node_min_count      = var.environment == "prod" ? 3 : 1
  app_node_max_count      = var.environment == "prod" ? 10 : 3
  tags                    = var.tags
}
