# Minimal AKS cluster for the ecommerce services: a system pool for add-ons, an autoscaled
# pool for the workloads, Azure CNI overlay with Calico network policies (so the
# NetworkPolicies in deploy/k8s are enforced), workload identity, Container Insights and an
# optional container registry the kubelet can pull from.

resource "azurerm_log_analytics_workspace" "this" {
  name                = "log-${var.name}"
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = var.tags
}

resource "azurerm_kubernetes_cluster" "this" {
  name                = var.name
  location            = var.location
  resource_group_name = var.resource_group_name
  dns_prefix          = var.name
  kubernetes_version  = var.kubernetes_version
  sku_tier            = var.sku_tier

  oidc_issuer_enabled               = true
  workload_identity_enabled         = true
  role_based_access_control_enabled = true

  default_node_pool {
    name                         = "system"
    vm_size                      = var.system_node_vm_size
    node_count                   = var.system_node_count
    zones                        = var.zones
    only_critical_addons_enabled = true
    os_disk_size_gb              = 64
    temporary_name_for_rotation  = "systemtmp"

    upgrade_settings {
      max_surge = "33%"
    }
  }

  identity {
    type = "SystemAssigned"
  }

  network_profile {
    network_plugin      = "azure"
    network_plugin_mode = "overlay"
    network_policy      = "calico"
    load_balancer_sku   = "standard"
  }

  oms_agent {
    log_analytics_workspace_id      = azurerm_log_analytics_workspace.this.id
    msi_auth_for_monitoring_enabled = true
  }

  tags = var.tags
}

resource "azurerm_kubernetes_cluster_node_pool" "apps" {
  name                  = "apps"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.this.id
  vm_size               = var.app_node_vm_size
  mode                  = "User"
  zones                 = var.zones
  auto_scaling_enabled  = true
  min_count             = var.app_node_min_count
  max_count             = var.app_node_max_count
  os_disk_size_gb       = 128

  upgrade_settings {
    max_surge = "33%"
  }

  tags = var.tags
}

resource "azurerm_container_registry" "this" {
  count = var.container_registry_name == null ? 0 : 1

  name                = var.container_registry_name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "Basic"
  admin_enabled       = false
  tags                = var.tags
}

# Lets the nodes pull images from the registry without image pull secrets.
resource "azurerm_role_assignment" "acr_pull" {
  count = var.container_registry_name == null ? 0 : 1

  scope                            = azurerm_container_registry.this[0].id
  role_definition_name             = "AcrPull"
  principal_id                     = azurerm_kubernetes_cluster.this.kubelet_identity[0].object_id
  skip_service_principal_aad_check = true
}
