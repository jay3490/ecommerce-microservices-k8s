output "id" {
  description = "Resource id of the vault."
  value       = azurerm_key_vault.this.id
}

output "name" {
  description = "Vault name, for a SecretProviderClass."
  value       = azurerm_key_vault.this.name
}

output "vault_uri" {
  description = "Vault URI."
  value       = azurerm_key_vault.this.vault_uri
}

output "tenant_id" {
  description = "Tenant of the vault, for a SecretProviderClass."
  value       = azurerm_key_vault.this.tenant_id
}

output "secret_names" {
  description = "Names of the stored secrets."
  value       = keys(azurerm_key_vault_secret.this)
}
