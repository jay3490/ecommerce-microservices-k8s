output "id" {
  description = "Resource id of the registry."
  value       = azurerm_container_registry.this.id
}

output "login_server" {
  description = "Registry host to push the images to."
  value       = azurerm_container_registry.this.login_server
}
