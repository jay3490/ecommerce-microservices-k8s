variable "name" {
  description = "Cache name, globally unique."
  type        = string
}

variable "resource_group_name" {
  description = "Resource group for the cache and its private endpoint."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "sku_name" {
  description = "Basic, Standard or Premium. Only Premium supports availability zones."
  type        = string
  default     = "Standard"

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.sku_name)
    error_message = "The sku_name must be Basic, Standard or Premium."
  }
}

variable "capacity" {
  description = "Cache size within the SKU family (C0-C6 for Basic/Standard, P1-P5 for Premium)."
  type        = number
  default     = 1
}

variable "zones" {
  description = "Zones for a Premium cache; its primary and replicas are spread over them."
  type        = list(string)
  default     = []
}

variable "replicas_per_primary" {
  description = "Replicas per primary for a Premium cache; two with three zones puts a copy in each."
  type        = number
  default     = 2
}

variable "virtual_network_id" {
  description = "Virtual network the private DNS zone is linked to."
  type        = string
}

variable "subnet_id" {
  description = "Subnet for the private endpoint."
  type        = string
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default     = {}
}
