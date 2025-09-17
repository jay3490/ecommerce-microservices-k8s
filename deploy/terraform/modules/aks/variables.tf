variable "name" {
  description = "Cluster name; also used as the DNS prefix."
  type        = string
}

variable "resource_group_name" {
  description = "Existing resource group to create the cluster in."
  type        = string
}

variable "location" {
  description = "Azure region, for example westeurope."
  type        = string
}

variable "kubernetes_version" {
  description = "AKS Kubernetes version. Null lets AKS pick its current default."
  type        = string
  default     = null
}

variable "sku_tier" {
  description = "AKS control plane tier: Free or Standard (uptime SLA)."
  type        = string
  default     = "Free"

  validation {
    condition     = contains(["Free", "Standard"], var.sku_tier)
    error_message = "The sku_tier must be Free or Standard."
  }
}

variable "automatic_upgrade_channel" {
  description = "AKS automatic upgrade channel: none, patch, stable, rapid or node-image."
  type        = string
  default     = "patch"
}

variable "zones" {
  description = "Availability zones. The system pool spans them and each zone gets its own application pool. Empty for regions without zones."
  type        = list(string)
  default     = ["1", "2", "3"]
}

variable "subnet_id" {
  description = "Subnet for the node pools."
  type        = string
}

variable "service_cidr" {
  description = "Kubernetes service address range; must not overlap the virtual network."
  type        = string
  default     = "10.240.0.0/16"
}

variable "dns_service_ip" {
  description = "Cluster DNS service address, inside service_cidr."
  type        = string
  default     = "10.240.0.10"
}

variable "log_analytics_workspace_id" {
  description = "Workspace that receives Container Insights data."
  type        = string
}

variable "system_node_vm_size" {
  description = "VM size of the system node pool, which runs only cluster add-ons."
  type        = string
  default     = "Standard_D2s_v5"
}

variable "system_node_count" {
  description = "Number of system nodes, spread over the zones."
  type        = number
  default     = 3
}

variable "app_node_vm_size" {
  description = "VM size of the application node pools."
  type        = string
  default     = "Standard_D4s_v5"
}

variable "app_node_min_count_per_zone" {
  description = "Minimum number of application nodes in each zonal pool."
  type        = number
  default     = 1
}

variable "app_node_max_count_per_zone" {
  description = "Maximum number of application nodes in each zonal pool."
  type        = number
  default     = 3

  validation {
    condition     = var.app_node_max_count_per_zone >= 1
    error_message = "The app_node_max_count_per_zone must be at least 1."
  }
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default     = {}
}
