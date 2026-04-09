# ── Log Analytics Workspace (shared by App Insights + Container Insights) ─────

resource "azurerm_log_analytics_workspace" "this" {
  name                = "${local.product_prefix}-${var.customer_name}-logs"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  sku                 = "PerGB2018"
  retention_in_days   = 30

  tags = azurerm_resource_group.this.tags
}

# ── Application Insights (for Azure Function logging) ────────────────────────

resource "azurerm_application_insights" "this" {
  name                = "${local.product_prefix}-${var.customer_name}-insights"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  workspace_id        = azurerm_log_analytics_workspace.this.id
  application_type    = "web"

  tags = azurerm_resource_group.this.tags
}

# ── Container Insights for AKS ──────────────────────────────────────────────
# The ContainerInsights solution is automatically deployed by the AKS oms_agent
# block in main.tf. No separate azurerm_log_analytics_solution resource needed.

# ── ACR Diagnostic Logging (for image pull tracking) ────────────────────────

resource "azurerm_monitor_diagnostic_setting" "acr" {
  name                       = "${local.product_prefix}-${var.customer_name}-acr-diag"
  target_resource_id         = azurerm_container_registry.this.id
  log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id

  enabled_log {
    category = "ContainerRegistryRepositoryEvents"
  }
}
