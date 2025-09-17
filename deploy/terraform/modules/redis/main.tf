# Azure Cache for Redis for the catalog cache, the baskets and the gateway's shared rate
# limit counters and output cache. TLS only, no public access: clients in the virtual network
# reach it through a private endpoint. Zones (and zone redundancy) need the Premium SKU.

resource "azurerm_redis_cache" "this" {
  name                          = var.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  sku_name                      = var.sku_name
  family                        = var.sku_name == "Premium" ? "P" : "C"
  capacity                      = var.capacity
  zones                         = var.sku_name == "Premium" && length(var.zones) > 0 ? var.zones : null
  replicas_per_primary          = var.sku_name == "Premium" ? var.replicas_per_primary : null
  non_ssl_port_enabled          = false
  minimum_tls_version           = "1.2"
  public_network_access_enabled = false

  redis_configuration {
    # Every key the services write has an expiry (cache entries, baskets, rate limit
    # windows); under memory pressure evict the least recently used of them.
    maxmemory_policy = "volatile-lru"
  }

  tags = var.tags
}

resource "azurerm_private_dns_zone" "this" {
  name                = "privatelink.redis.cache.windows.net"
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

resource "azurerm_private_endpoint" "this" {
  name                = "pe-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  subnet_id           = var.subnet_id

  private_service_connection {
    name                           = "redis"
    private_connection_resource_id = azurerm_redis_cache.this.id
    subresource_names              = ["redisCache"]
    is_manual_connection           = false
  }

  private_dns_zone_group {
    name                 = "redis"
    private_dns_zone_ids = [azurerm_private_dns_zone.this.id]
  }

  tags = var.tags
}
