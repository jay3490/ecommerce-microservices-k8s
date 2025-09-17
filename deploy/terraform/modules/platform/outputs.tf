output "cluster_name" {
  description = "AKS cluster; use with az aks get-credentials."
  value       = module.aks.cluster_name
}

output "oidc_issuer_url" {
  description = "OIDC issuer URL for workload identity federation."
  value       = module.aks.oidc_issuer_url
}

output "app_node_pools" {
  description = "Application node pools by zone."
  value       = module.aks.app_node_pools
}

output "container_registry_login_server" {
  description = "Registry to push the service images to."
  value       = module.acr.login_server
}

output "postgres_fqdn" {
  description = "Private host name of the PostgreSQL server."
  value       = module.postgres.fqdn
}

output "redis_hostname" {
  description = "Host name of the Redis cache."
  value       = module.redis.hostname
}

output "key_vault_name" {
  description = "Key Vault holding the connection strings."
  value       = module.key_vault.name
}

output "key_vault_tenant_id" {
  description = "Tenant of the Key Vault."
  value       = module.key_vault.tenant_id
}

output "key_vault_secrets_provider_client_id" {
  description = "Client id for a SecretProviderClass that syncs the Key Vault secrets into ecommerce-secrets."
  value       = module.aks.key_vault_secrets_provider_client_id
}

output "log_analytics_workspace_id" {
  description = "Workspace receiving Container Insights data."
  value       = module.log_analytics.id
}
