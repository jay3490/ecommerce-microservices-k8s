# Non-secret settings for dev; secrets are generated and kept in Key Vault. The two names
# below must be globally unique, so change them before the first apply.
location                = "westeurope"
zones                   = ["1"]
container_registry_name = "acrecommercedev01"
key_vault_name          = "kv-ecommerce-dev-01"
