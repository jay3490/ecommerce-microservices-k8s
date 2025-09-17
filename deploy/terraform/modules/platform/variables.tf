variable "name" {
  description = "Base name for the resources, for example ecommerce-prod."
  type        = string
}

variable "resource_group_name" {
  description = "Existing resource group for every resource."
  type        = string
}

variable "location" {
  description = "Azure region; it must support availability zones when zones is not empty."
  type        = string
}

variable "zones" {
  description = "Availability zones used by the node pools, PostgreSQL HA, a Premium Redis and a Premium registry."
  type        = list(string)
  default     = ["1", "2", "3"]
}

variable "address_space" {
  description = "Virtual network range (a /16 is split into the node, PostgreSQL and private endpoint subnets)."
  type        = string
  default     = "10.20.0.0/16"
}

variable "databases" {
  description = "One PostgreSQL database per service that uses PostgreSQL."
  type        = list(string)
  default     = ["catalog", "ordering", "inventory", "payment", "identity", "shipping", "notification"]
}

variable "kubernetes_version" {
  description = "AKS version; null for the current default."
  type        = string
  default     = null
}

variable "aks_sku_tier" {
  description = "AKS control plane tier: Free or Standard."
  type        = string
  default     = "Free"
}

variable "system_node_count" {
  description = "System nodes, spread over the zones."
  type        = number
  default     = 3
}

variable "app_node_vm_size" {
  description = "VM size of the zonal application pools."
  type        = string
  default     = "Standard_D4s_v5"
}

variable "app_node_min_count_per_zone" {
  description = "Minimum application nodes per zone."
  type        = number
  default     = 1
}

variable "app_node_max_count_per_zone" {
  description = "Maximum application nodes per zone."
  type        = number
  default     = 3
}

variable "container_registry_name" {
  description = "Globally unique, alphanumeric registry name."
  type        = string
}

variable "container_registry_sku" {
  description = "Registry SKU; Premium adds zone redundancy."
  type        = string
  default     = "Standard"
}

variable "postgres_sku_name" {
  description = "PostgreSQL compute SKU; HA needs General Purpose or Memory Optimized."
  type        = string
  default     = "GP_Standard_D2ds_v5"
}

variable "postgres_storage_mb" {
  description = "PostgreSQL storage in MB."
  type        = number
  default     = 65536
}

variable "postgres_high_availability" {
  description = "Zone-redundant HA for PostgreSQL (standby in the second zone)."
  type        = bool
  default     = true
}

variable "postgres_geo_redundant_backup" {
  description = "Copy PostgreSQL backups to the paired region."
  type        = bool
  default     = false
}

variable "redis_sku_name" {
  description = "Redis SKU; Premium spreads the cache over the zones."
  type        = string
  default     = "Standard"
}

variable "redis_capacity" {
  description = "Redis size within the SKU family."
  type        = number
  default     = 1
}

variable "key_vault_name" {
  description = "Globally unique Key Vault name, 3 to 24 characters."
  type        = string
}

variable "key_vault_purge_protection" {
  description = "Purge protection (90-day retention); leave off for throwaway environments."
  type        = bool
  default     = true
}

variable "key_vault_allowed_ip_ranges" {
  description = "Public IP ranges allowed to reach Key Vault; null allows all networks."
  type        = list(string)
  default     = null
}

variable "log_retention_days" {
  description = "Log Analytics retention."
  type        = number
  default     = 30
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default     = {}
}
