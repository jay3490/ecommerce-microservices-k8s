variable "name" {
  description = "Vault name, globally unique, 3 to 24 characters."
  type        = string

  validation {
    condition     = can(regex("^[a-zA-Z][a-zA-Z0-9-]{1,22}[a-zA-Z0-9]$", var.name))
    error_message = "Key Vault names are 3 to 24 letters, digits and hyphens, starting with a letter."
  }
}

variable "resource_group_name" {
  description = "Resource group for the vault."
  type        = string
}

variable "location" {
  description = "Azure region."
  type        = string
}

variable "purge_protection_enabled" {
  description = "Prevent permanent deletion during the retention period. Cannot be turned off once on."
  type        = bool
  default     = true
}

variable "soft_delete_retention_days" {
  description = "Days a deleted vault or secret can be recovered (7 to 90)."
  type        = number
  default     = 90
}

variable "allowed_ip_ranges" {
  description = "Public IP ranges allowed to reach the vault, for example the deployment agents. Null allows all networks."
  type        = list(string)
  default     = null
}

variable "readers" {
  description = "Principals that may read secrets (Key Vault Secrets User): object ids keyed by a fixed name, so the keys are known at plan time."
  type        = map(string)
  default     = {}
}

variable "secrets" {
  description = "Secrets to store, keyed by secret name."
  type        = map(string)
  sensitive   = true
  default     = {}
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default     = {}
}
