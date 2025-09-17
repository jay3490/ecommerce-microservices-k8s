output "id" {
  description = "Resource id of the cache."
  value       = azurerm_redis_cache.this.id
}

output "hostname" {
  description = "Host name of the cache (resolves to the private endpoint inside the network)."
  value       = azurerm_redis_cache.this.hostname
}

output "connection_string" {
  description = "StackExchange.Redis connection string over TLS."
  value       = "${azurerm_redis_cache.this.hostname}:${azurerm_redis_cache.this.ssl_port},password=${azurerm_redis_cache.this.primary_access_key},ssl=True,abortConnect=False"
  sensitive   = true
}
