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
  os_type             = "Linux"
  sku_name            = "Y1"
}

# ── Azure Function App ───────────────────────────────────────────────────────

resource "azurerm_linux_function_app" "this" {
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
      dotnet_version              = "8.0"
      use_dotnet_isolated_runtime = true
    }
  }

  app_settings = {
    FUNCTIONS_WORKER_RUNTIME = "dotnet-isolated"
    AZURE_CLIENT_ID          = azurerm_user_assigned_identity.function.client_id
    AKS_SUBSCRIPTION_ID      = var.subscription_id
    AKS_RESOURCE_GROUP       = azurerm_resource_group.this.name
    AKS_CLUSTER_NAME         = azurerm_kubernetes_cluster.this.name
    ALLOWED_ORG_TEAMS        = jsonencode(var.allowed_org_teams)
  }

  tags = azurerm_resource_group.this.tags
}
