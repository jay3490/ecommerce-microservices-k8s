# Non-secret settings for prod; secrets are generated and kept in Key Vault. The two names
# below must be globally unique, so change them before the first apply.
location                = "westeurope"
zones                   = ["1", "2", "3"]
container_registry_name = "acrecommerceprod01"
key_vault_name          = "kv-ecommerce-prod-01"
