# ── Log Analytics Workspace (shared by App Insights + Container Insights) ─────

resource "azurerm_log_analytics_workspace" "this" {
  name                = "${local.product_prefix}-${var.org_name}-logs"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  sku                 = "PerGB2018"
  retention_in_days   = 30

  tags = azurerm_resource_group.this.tags
}

# ── Application Insights (for Azure Function logging) ────────────────────────

resource "azurerm_application_insights" "this" {
  name                = "${local.product_prefix}-${var.org_name}-insights"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  workspace_id        = azurerm_log_analytics_workspace.this.id
  application_type    = "web"

  tags = azurerm_resource_group.this.tags
}

# ── Container Insights for AKS ──────────────────────────────────────────────
# The ContainerInsights solution is automatically deployed by the AKS oms_agent
# block in main.tf. No separate azurerm_log_analytics_solution resource needed.

# ── ACR Diagnostic Logging ───────────────────────────────────────────────────
# Azure Policy auto-provisions a diagnostic setting on the ACR.
# Do NOT manage it in Terraform — it causes "already exists" conflicts.
