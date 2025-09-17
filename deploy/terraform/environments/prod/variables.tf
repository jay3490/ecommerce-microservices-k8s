variable "subscription_id" {
  description = "Azure subscription id. Null uses ARM_SUBSCRIPTION_ID."
  type        = string
  default     = null
}

variable "location" {
  description = "Azure region."
  type        = string
  default     = "westeurope"
}

variable "zones" {
  description = "Availability zones: node pools, PostgreSQL HA, Redis and the registry span them."
  type        = list(string)
  default     = ["1", "2", "3"]

  validation {
    condition     = length(var.zones) >= 2
    error_message = "Production needs at least two zones (three recommended)."
  }
}

variable "container_registry_name" {
  description = "Globally unique, alphanumeric registry name."
  type        = string
}

variable "key_vault_name" {
  description = "Globally unique Key Vault name, 3 to 24 characters."
  type        = string
}

variable "key_vault_allowed_ip_ranges" {
  description = "Public IP ranges (for example the deployment agents) allowed to reach Key Vault; null allows all."
  type        = list(string)
  default     = null
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default = {
    project    = "ecommerce-microservices-k8s"
    managed-by = "terraform"
  }
}
