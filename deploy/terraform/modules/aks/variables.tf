variable "name" {
  description = "Cluster name; also used to derive the names of related resources."
  type        = string
}

variable "resource_group_name" {
  description = "Existing resource group to create the resources in."
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

variable "zones" {
  description = "Availability zones for the node pools. Empty for regions without zones."
  type        = list(string)
  default     = ["1", "2", "3"]
}

variable "system_node_vm_size" {
  description = "VM size of the system node pool, which runs only cluster add-ons."
  type        = string
  default     = "Standard_D2s_v5"
}

variable "system_node_count" {
  description = "Number of system nodes."
  type        = number
  default     = 2
}

variable "app_node_vm_size" {
  description = "VM size of the autoscaled node pool that runs the application."
  type        = string
  default     = "Standard_D4s_v5"
}

variable "app_node_min_count" {
  description = "Minimum number of application nodes."
  type        = number
  default     = 2
}

variable "app_node_max_count" {
  description = "Maximum number of application nodes."
  type        = number
  default     = 5

  validation {
    condition     = var.app_node_max_count >= 1
    error_message = "The app_node_max_count must be at least 1."
  }
}

variable "container_registry_name" {
  description = "Globally unique, alphanumeric name for an Azure Container Registry the cluster can pull from. Null skips creating one."
  type        = string
  default     = null
}

variable "log_retention_days" {
  description = "Retention of the Log Analytics workspace used by Container Insights."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default     = {}
}
