# AKS cluster for the ecommerce services: a system pool for add-ons spread over the zones,
# and one autoscaled application pool per availability zone. Separate zonal pools let the
# cluster autoscaler add a node in the zone that needs it, which a single pool spanning all
# zones cannot guarantee, and keep the topology spread constraints in the manifests
# satisfiable. Azure CNI overlay with Calico enforces the NetworkPolicies; workload identity
# and the Key Vault secrets provider are enabled.

resource "azurerm_kubernetes_cluster" "this" {
  name                      = var.name
  location                  = var.location
  resource_group_name       = var.resource_group_name
  dns_prefix                = var.name
  kubernetes_version        = var.kubernetes_version
  sku_tier                  = var.sku_tier
  automatic_upgrade_channel = var.automatic_upgrade_channel

  oidc_issuer_enabled               = true
  workload_identity_enabled         = true
  role_based_access_control_enabled = true

  default_node_pool {
    name                         = "system"
    vm_size                      = var.system_node_vm_size
    node_count                   = var.system_node_count
    zones                        = var.zones
    vnet_subnet_id               = var.subnet_id
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
    service_cidr        = var.service_cidr
    dns_service_ip      = var.dns_service_ip
  }

  key_vault_secrets_provider {
    secret_rotation_enabled = true
  }

  oms_agent {
    log_analytics_workspace_id      = var.log_analytics_workspace_id
    msi_auth_for_monitoring_enabled = true
  }

  tags = var.tags
}

# One pool per zone, for example apps1, apps2 and apps3 (a single "apps0" without zones).
resource "azurerm_kubernetes_cluster_node_pool" "apps" {
  for_each = toset(length(var.zones) > 0 ? var.zones : ["0"])

  name                  = "apps${each.key}"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.this.id
  vm_size               = var.app_node_vm_size
  mode                  = "User"
  zones                 = length(var.zones) > 0 ? [each.key] : null
  vnet_subnet_id        = var.subnet_id
  auto_scaling_enabled  = true
  min_count             = var.app_node_min_count_per_zone
  max_count             = var.app_node_max_count_per_zone
  os_disk_size_gb       = 128

  node_labels = {
    "workload" = "apps"
  }

  upgrade_settings {
    max_surge = "33%"
  }

  tags = var.tags
}
