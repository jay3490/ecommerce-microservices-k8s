variable "subscription_id" {
  description = "Azure subscription id. Null uses ARM_SUBSCRIPTION_ID."
  type        = string
  default     = null
}

variable "environment" {
  description = "Short environment name used in resource names, for example dev or prod."
  type        = string
  default     = "dev"
}

variable "location" {
  description = "Azure region."
  type        = string
  default     = "westeurope"
}

variable "container_registry_name" {
  description = "Globally unique alphanumeric registry name, or null to skip the registry."
  type        = string
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
