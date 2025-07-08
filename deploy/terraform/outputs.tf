output "resource_group_name" {
  description = "Resource group holding the cluster."
  value       = azurerm_resource_group.this.name
}

output "cluster_name" {
  description = "Name of the AKS cluster; use with az aks get-credentials."
  value       = module.aks.cluster_name
}

output "oidc_issuer_url" {
  description = "OIDC issuer URL for workload identity federation."
  value       = module.aks.oidc_issuer_url
}

output "container_registry_login_server" {
  description = "Registry to push the service images to, if one was created."
  value       = module.aks.container_registry_login_server
}
