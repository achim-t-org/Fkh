# ── Storage Account (database backups) ────────────────────────────────────────

resource "azurerm_storage_account" "dbs" {
  name                     = local.dbs_storage_name
  resource_group_name      = azurerm_resource_group.this.name
  location                 = azurerm_resource_group.this.location
  account_tier             = "Standard"
  account_replication_type = "LRS"

  tags = azurerm_resource_group.this.tags
}

# ── Storage Account (required by Azure Functions runtime) ─────────────────────

resource "azurerm_storage_account" "function" {
  name                     = local.function_storage_name
  resource_group_name      = azurerm_resource_group.this.name
  location                 = azurerm_resource_group.this.location
  account_tier             = "Standard"
  account_replication_type = "LRS"

  tags = azurerm_resource_group.this.tags
}

# ── App Service Plan (Consumption = pay-per-execution, no idle cost) ──────────

resource "azurerm_service_plan" "function" {
  name                = local.function_plan_name
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  os_type             = "Windows"
  sku_name            = "Y1"
}

# ── Azure Function App ───────────────────────────────────────────────────────

resource "azurerm_windows_function_app" "this" {
  name                = local.function_app_name
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location

  storage_account_name       = azurerm_storage_account.function.name
  storage_account_access_key = azurerm_storage_account.function.primary_access_key
  service_plan_id            = azurerm_service_plan.function.id

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.function.id]
  }

  site_config {
    application_stack {
      dotnet_version              = "v8.0"
      use_dotnet_isolated_runtime = true
    }
  }

  app_settings = {
    FUNCTIONS_WORKER_RUNTIME                = "dotnet-isolated"
    AZURE_CLIENT_ID                         = azurerm_user_assigned_identity.function.client_id
    AKS_SUBSCRIPTION_ID                     = var.subscription_id
    AKS_RESOURCE_GROUP                      = azurerm_resource_group.this.name
    AKS_CLUSTER_NAME                        = azurerm_kubernetes_cluster.this.name
    ACR_NAME                                = azurerm_container_registry.this.name
    BASE_IMAGE                              = var.base_image
    ALLOWED_ORG_TEAMS                       = jsonencode(var.allowed_org_teams)
    ADMIN_ORG_TEAMS                          = jsonencode(var.admin_org_teams)
    ALLOWED_OIDC_REPOS                       = jsonencode(var.allowed_oidc_repos)
    AKS_LOCATION                             = var.location
    CONTACT_EMAIL_FOR_LETSENCRYPT             = var.contact_email_for_letsencrypt
    GITHUB_APP_ID                            = var.github_app_id
    GITHUB_APP_PRIVATE_KEY                   = var.github_app_private_key
    GITHUB_APP_INSTALLATION_ID               = var.github_app_installation_id
    GITHUB_REPO_OWNER                        = var.github_org
    GITHUB_REPO_NAME                         = var.github_repo
    DBS_STORAGE_ACCOUNT_NAME                 = azurerm_storage_account.dbs.name
    LOG_ANALYTICS_WORKSPACE_ID               = azurerm_log_analytics_workspace.this.id
    APPINSIGHTS_INSTRUMENTATIONKEY          = azurerm_application_insights.this.instrumentation_key
    APPLICATIONINSIGHTS_CONNECTION_STRING   = azurerm_application_insights.this.connection_string
  }

  tags = azurerm_resource_group.this.tags
}
