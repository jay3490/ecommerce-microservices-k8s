output "server_id" {
  description = "Resource id of the server."
  value       = azurerm_postgresql_flexible_server.this.id
}

output "fqdn" {
  description = "Private host name of the server."
  value       = azurerm_postgresql_flexible_server.this.fqdn
}

output "administrator_login" {
  description = "Administrator user name."
  value       = azurerm_postgresql_flexible_server.this.administrator_login
}

output "administrator_password" {
  description = "Generated administrator password."
  value       = random_password.administrator.result
  sensitive   = true
}

output "connection_strings" {
  description = "Npgsql connection strings, keyed by database."
  sensitive   = true

  value = {
    for database in var.databases : database => join(";", [
      "Host=${azurerm_postgresql_flexible_server.this.fqdn}",
      "Port=5432",
      "Database=${database}",
      "Username=${azurerm_postgresql_flexible_server.this.administrator_login}",
      "Password=${random_password.administrator.result}",
      "SSL Mode=Require",
    ])
  }
}
