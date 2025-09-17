# Container registry for the service images. Admin credentials stay off; the cluster pulls
# with its kubelet identity (AcrPull, assigned in the platform module). Premium can replicate
# the registry across the region's availability zones.

resource "azurerm_container_registry" "this" {
  name                          = var.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  sku                           = var.sku
  admin_enabled                 = false
  zone_redundancy_enabled       = var.sku == "Premium" ? var.zone_redundancy_enabled : false
  public_network_access_enabled = true
  tags                          = var.tags
}
