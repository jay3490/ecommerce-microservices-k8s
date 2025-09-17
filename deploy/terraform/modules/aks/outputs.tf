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
  description = "Object id of the kubelet managed identity (pulls images)."
  value       = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
}

output "key_vault_secrets_provider_object_id" {
  description = "Object id of the identity the Key Vault CSI driver uses."
  value       = azurerm_kubernetes_cluster.this.key_vault_secrets_provider[0].secret_identity[0].object_id
}

output "key_vault_secrets_provider_client_id" {
  description = "Client id of the identity the Key Vault CSI driver uses (for a SecretProviderClass)."
  value       = azurerm_kubernetes_cluster.this.key_vault_secrets_provider[0].secret_identity[0].client_id
}

output "app_node_pools" {
  description = "Application node pool names, keyed by zone."
  value       = { for zone, pool in azurerm_kubernetes_cluster_node_pool.apps : zone => pool.name }
}

output "kube_config" {
  description = "Admin kubeconfig for the cluster."
  value       = azurerm_kubernetes_cluster.this.kube_config_raw
  sensitive   = true
}
