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
  description = "Availability zones; dev uses one to keep the node count low."
  type        = list(string)
  default     = ["1"]
}

variable "container_registry_name" {
  description = "Globally unique, alphanumeric registry name."
  type        = string
}

variable "key_vault_name" {
  description = "Globally unique Key Vault name, 3 to 24 characters."
  type        = string
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default = {
    project    = "ecommerce-microservices-k8s"
    managed-by = "terraform"
  }
}
