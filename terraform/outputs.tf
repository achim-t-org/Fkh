output "cluster_name" {
  description = "The AKS cluster name."
  value       = azurerm_kubernetes_cluster.this.name
}

output "resource_group_name" {
  description = "The resource group name."
  value       = azurerm_resource_group.this.name
}

output "location" {
  description = "The Azure region."
  value       = azurerm_resource_group.this.location
}

output "namespace" {
  description = "The Kubernetes namespace."
  value       = kubernetes_namespace.workload.metadata[0].name
}

output "sql_service_fqdn" {
  description = "Internal FQDN for the SQL Server service."
  value       = "mssql-service.${var.namespace}.svc.cluster.local:1433"
}

output "function_app_name" {
  description = "Name of the Azure Function App. Used by deploy-functionupdate.ps1 to publish code."
  value       = azurerm_windows_function_app.this.name
}

output "function_url" {
  description = "Base URL of the Azure Function. Paste this into FUNCTION_URL in the VS Code extension."
  value       = "https://${azurerm_windows_function_app.this.default_hostname}/api/CreateNode"
}

output "github_team_url" {
  description = "URL to the GitHub team for managing access."
  value       = "https://github.com/orgs/${var.github_org}/teams/${github_team.provisioners.slug}"
}

output "acr_login_server" {
  description = "ACR login server for docker login."
  value       = azurerm_container_registry.this.login_server
}

output "identity_client_id" {
  description = "Client ID of the managed identity (used for OIDC federation)."
  value       = azurerm_user_assigned_identity.function.client_id
}

output "tenant_id" {
  description = "Azure AD tenant ID (used for OIDC federation)."
  value       = var.tenant_id
}

output "subscription_id" {
  description = "Azure subscription ID (used for OIDC federation)."
  value       = var.subscription_id
}

output "github_repo" {
  description = "GitHub org/repo for Actions secret sync."
  value       = "${var.github_org}/${var.github_repo}"
}

output "managed_identity_client_id" {
  description = "Client ID of the Function's Managed Identity."
  value       = azurerm_user_assigned_identity.function.client_id
}

output "kube_config_command" {
  description = "Command to fetch kubeconfig."
  value       = "az aks get-credentials --resource-group ${azurerm_resource_group.this.name} --name ${azurerm_kubernetes_cluster.this.name} --overwrite-existing"
}

output "acr_name" {
  description = "Name of the Azure Container Registry."
  value       = azurerm_container_registry.this.name
}
