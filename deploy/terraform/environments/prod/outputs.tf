output "resource_group_name" {
  description = "Resource group holding the environment."
  value       = azurerm_resource_group.this.name
}

output "cluster_name" {
  description = "AKS cluster; use with az aks get-credentials."
  value       = module.platform.cluster_name
}

output "container_registry_login_server" {
  description = "Registry to push the service images to."
  value       = module.platform.container_registry_login_server
}

output "postgres_fqdn" {
  description = "Private host name of the PostgreSQL server."
  value       = module.platform.postgres_fqdn
}

output "app_node_pools" {
  description = "Application node pools by zone."
  value       = module.platform.app_node_pools
}

output "key_vault_name" {
  description = "Key Vault holding the connection strings."
  value       = module.platform.key_vault_name
}

output "key_vault_secrets_provider_client_id" {
  description = "Client id for the SecretProviderClass."
  value       = module.platform.key_vault_secrets_provider_client_id
}
