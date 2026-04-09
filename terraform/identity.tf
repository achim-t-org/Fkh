# ── Managed Identity for the Azure Function ───────────────────────────────────
#
# This identity is what the Function uses to talk to AKS.
# No credentials, no secrets — Azure handles authentication at the infrastructure level.

resource "azurerm_user_assigned_identity" "function" {
  name                = local.function_identity_name
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
}

# Grant the Function's identity the "Azure Kubernetes Service Contributor" role
# on the AKS cluster.
resource "azurerm_role_assignment" "function_aks" {
  scope                = azurerm_kubernetes_cluster.this.id
  role_definition_name = "Azure Kubernetes Service Contributor Role"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
}

# Grant the Function's identity "Storage Blob Data Contributor" on the dbs
# storage account so the createImages workflow can upload database backups.
resource "azurerm_role_assignment" "function_dbs_storage" {
  scope                = azurerm_storage_account.dbs.id
  role_definition_name = "Storage Blob Data Contributor"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
}

# Grant the Function's identity "Log Analytics Reader" so it can query
# ContainerRegistryRepositoryEvents for image pull timestamps.
resource "azurerm_role_assignment" "function_log_analytics_reader" {
  scope                = azurerm_log_analytics_workspace.this.id
  role_definition_name = "Log Analytics Reader"
  principal_id         = azurerm_user_assigned_identity.function.principal_id
}

# ── Federated credential for GitHub Actions OIDC ──────────────────────────────
# Allows the createImages workflow in the configured repo to authenticate
# as the managed identity and push images to ACR.

resource "azurerm_federated_identity_credential" "github_actions" {
  name                = "github-actions-createimages"
  parent_id           = azurerm_user_assigned_identity.function.id
  audience            = ["api://AzureADTokenExchange"]
  issuer              = "https://token.actions.githubusercontent.com"
  subject             = "repo:${var.github_org}/${var.github_repo}:ref:refs/heads/main"
}
