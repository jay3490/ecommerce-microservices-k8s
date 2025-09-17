variable "name" {
  description = "Server name, globally unique."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group for the server and its private DNS zone."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "virtual_network_id" {
  description = "Virtual network the private DNS zone is linked to."
  type        = string
}

variable "subnet_id" {
  description = "Subnet delegated to Microsoft.DBforPostgreSQL/flexibleServers."
  type        = string
}

variable "postgres_version" {
  description = "PostgreSQL major version."
  type        = string
  default     = "16"
}

variable "sku_name" {
  description = "Compute SKU, for example B_Standard_B1ms (dev) or GP_Standard_D2ds_v5."
  type        = string
  default     = "GP_Standard_D2ds_v5"
}

variable "storage_mb" {
  description = "Allocated storage in MB."
  type        = number
  default     = 65536
}

variable "backup_retention_days" {
  description = "Point-in-time restore window in days (7 to 35)."
  type        = number
  default     = 14
}

variable "geo_redundant_backup_enabled" {
  description = "Copy backups to the paired region."
  type        = bool
  default     = false
}

variable "high_availability_enabled" {
  description = "Zone-redundant HA with a standby in standby_zone."
  type        = bool
  default     = true
}

variable "primary_zone" {
  description = "Zone of the primary server."
  type        = string
  default     = "1"
}

variable "standby_zone" {
  description = "Zone of the HA standby; must differ from primary_zone."
  type        = string
  default     = "2"
}

variable "administrator_login" {
  description = "Administrator user name. Its password is generated and stored in Key Vault by the platform module."
  type        = string
  default     = "ecommerceadmin"
}

variable "databases" {
  description = "Databases to create, one per service."
  type        = list(string)
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default     = {}
}
