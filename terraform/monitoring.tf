# ── Log Analytics Workspace (shared by App Insights + Container Insights) ─────

resource "azurerm_log_analytics_workspace" "this" {
  name                = "${local.product_prefix}-${var.fkhDeploymentName}-logs"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  sku                 = "PerGB2018"
  retention_in_days   = 30

  tags = azurerm_resource_group.this.tags
}

# ── Application Insights (for Azure Function logging) ────────────────────────

resource "azurerm_application_insights" "this" {
  name                = "${local.product_prefix}-${var.fkhDeploymentName}-insights"
  resource_group_name = azurerm_resource_group.this.name
  location            = azurerm_resource_group.this.location
  workspace_id        = azurerm_log_analytics_workspace.this.id
  application_type    = "web"

  tags = azurerm_resource_group.this.tags
}

# ── Container Insights for AKS ──────────────────────────────────────────────
# The ContainerInsights solution is automatically deployed by the AKS oms_agent
# block in main.tf. No separate azurerm_log_analytics_solution resource needed.

# ── Kubecost (free tier) — per-pod / per-namespace cost allocation ───────────

resource "helm_release" "kubecost" {
  count            = var.kubecost_enabled ? 1 : 0
  name             = "kubecost"
  repository       = "https://kubecost.github.io/kubecost/"
  chart            = "kubecost"
  namespace        = "kubecost"
  create_namespace = true
  wait             = false

  set {
    name  = "global.clusterId"
    value = azurerm_kubernetes_cluster.this.name
  }

  # Pin all Kubecost pods to Linux nodes (cluster also has Windows nodes)
  set {
    name  = "global.platforms.openshift.enabled"
    value = "false"
  }
  set {
    name  = "nodeSelector.kubernetes\\.io/os"
    value = "linux"
  }
  set {
    name  = "networkCosts.nodeSelector.kubernetes\\.io/os"
    value = "linux"
  }
  set {
    name  = "forecasting.nodeSelector.kubernetes\\.io/os"
    value = "linux"
  }
  set {
    name  = "finopsAgent.nodeSelector.kubernetes\\.io/os"
    value = "linux"
  }
  set {
    name  = "aggregator.nodeSelector.kubernetes\\.io/os"
    value = "linux"
  }
  set {
    name  = "cloudCost.nodeSelector.kubernetes\\.io/os"
    value = "linux"
  }

  # Reduce resource requests to fit on a shared node with SQL Server
  set {
    name  = "aggregator.resources.requests.memory"
    value = "512Mi"
  }
  set {
    name  = "aggregator.resources.requests.cpu"
    value = "50m"
  }
  set {
    name  = "forecasting.resources.requests.memory"
    value = "128Mi"
  }
  set {
    name  = "forecasting.resources.requests.cpu"
    value = "10m"
  }
  set {
    name  = "finopsAgent.resources.requests.memory"
    value = "64Mi"
  }
  set {
    name  = "finopsAgent.resources.requests.cpu"
    value = "10m"
  }

  depends_on = [azurerm_kubernetes_cluster.this]
}

# ── ACR Diagnostic Logging ───────────────────────────────────────────────────
# Azure Policy auto-provisions a diagnostic setting on the ACR.
# Do NOT manage it in Terraform — it causes "already exists" conflicts.
