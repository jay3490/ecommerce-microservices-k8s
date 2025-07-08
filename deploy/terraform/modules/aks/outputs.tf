output "cluster_id" {
  description = "Resource id of the AKS cluster."
  value       = azurerm_kubernetes_cluster.this.id
}

output "cluster_name" {
  description = "Name of the AKS cluster."
  value       = azurerm_kubernetes_cluster.this.name
}

output "oidc_issuer_url" {
  description = "OIDC issuer URL, for federating workload identities."
  value       = azurerm_kubernetes_cluster.this.oidc_issuer_url
}

output "kubelet_identity_object_id" {
  description = "Object id of the kubelet managed identity."
  value       = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
}

output "kube_config" {
  description = "Admin kubeconfig for the cluster."
  value       = azurerm_kubernetes_cluster.this.kube_config_raw
  sensitive   = true
}

output "container_registry_login_server" {
  description = "Login server of the registry, or null when none was created."
  value       = one(azurerm_container_registry.this[*].login_server)
}

output "log_analytics_workspace_id" {
  description = "Workspace receiving Container Insights data."
  value       = azurerm_log_analytics_workspace.this.id
}
