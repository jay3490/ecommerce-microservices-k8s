output "id" {
  description = "Resource id of the workspace."
  value       = azurerm_log_analytics_workspace.this.id
}

output "workspace_id" {
  description = "Workspace (customer) id."
  value       = azurerm_log_analytics_workspace.this.workspace_id
}
