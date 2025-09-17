# Key Vault holding the services' connection strings, with Azure RBAC instead of access
# policies. The identity running Terraform gets Key Vault Secrets Officer so it can write the
# secrets; the readers (the AKS Key Vault CSI driver identity) get Key Vault Secrets User,
# which lets a SecretProviderClass sync them into the ecommerce-secrets Secret. Role
# assignments can take a few minutes to apply, so a first apply may need to be repeated.

data "azurerm_client_config" "current" {}

resource "azurerm_key_vault" "this" {
  name                          = var.name
  location                      = var.location
  resource_group_name           = var.resource_group_name
  tenant_id                     = data.azurerm_client_config.current.tenant_id
  sku_name                      = "standard"
  enable_rbac_authorization     = true
  purge_protection_enabled      = var.purge_protection_enabled
  soft_delete_retention_days    = var.soft_delete_retention_days
  public_network_access_enabled = true

  network_acls {
    bypass         = "AzureServices"
    default_action = var.allowed_ip_ranges == null ? "Allow" : "Deny"
    ip_rules       = var.allowed_ip_ranges == null ? [] : var.allowed_ip_ranges
  }

  tags = var.tags
}

resource "azurerm_role_assignment" "deployer" {
  scope                = azurerm_key_vault.this.id
  role_definition_name = "Key Vault Secrets Officer"
  principal_id         = data.azurerm_client_config.current.object_id
}

resource "azurerm_role_assignment" "readers" {
  for_each = var.readers

  scope                            = azurerm_key_vault.this.id
  role_definition_name             = "Key Vault Secrets User"
  principal_id                     = each.value
  skip_service_principal_aad_check = true
}

resource "azurerm_key_vault_secret" "this" {
  for_each = nonsensitive(toset(keys(var.secrets)))

  name         = each.key
  value        = var.secrets[each.key]
  key_vault_id = azurerm_key_vault.this.id
  content_type = "text/plain"

  depends_on = [azurerm_role_assignment.deployer]
}
