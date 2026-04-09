terraform {
  required_version = ">= 1.7.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 2.47"
    }
    github = {
      source  = "integrations/github"
      version = "~> 6.0"
    }
    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = "~> 2.30"
    }
  }

  backend "azurerm" {}
}

provider "azurerm" {
  subscription_id = var.subscription_id
  features {}
}

provider "azuread" {
  tenant_id = var.tenant_id
}

provider "github" {
  owner = var.github_org
  token = var.github_token
}

locals {
  product_prefix              = "fkh"
  storage_account_org_id = substr(replace(var.org_name, "-", ""), 0, 14)

  resource_group_name    = "${local.product_prefix}-${var.org_name}"
  aks_cluster_name       = "${local.product_prefix}-${var.org_name}-aks"
  aks_dns_prefix         = local.aks_cluster_name
  function_plan_name     = "${local.product_prefix}-${var.org_name}-plan"
  function_app_name      = "${local.product_prefix}-${var.org_name}-backend"
  function_identity_name = "${local.product_prefix}-${var.org_name}-identity"
  function_storage_name  = "${local.product_prefix}${local.storage_account_org_id}func"
  dbs_storage_name       = "${local.product_prefix}${local.storage_account_org_id}dbs"
}

# ============================================================================
# Resource Group
# ============================================================================

resource "azurerm_resource_group" "this" {
  name     = local.resource_group_name
  location = var.location

  tags = {
    organization = var.org_name
    environment = "prod"
    managed_by  = "terraform"
  }
}

# ============================================================================
# AKS Cluster with Linux system node pool
# ============================================================================

resource "azurerm_kubernetes_cluster" "this" {
  name                = local.aks_cluster_name
  location            = azurerm_resource_group.this.location
  resource_group_name = azurerm_resource_group.this.name
  dns_prefix          = local.aks_dns_prefix
  sku_tier            = var.aks_sku_tier
  oidc_issuer_enabled = true

  default_node_pool {
    name       = "linuxpool"
    node_count = 1
    vm_size    = var.linux_vm_size
    os_sku     = "Ubuntu"
  }

  network_profile {
    network_plugin      = "azure"
    network_plugin_mode = "overlay"
  }

  identity {
    type = "SystemAssigned"
  }

  oms_agent {
    log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
  }

  tags = azurerm_resource_group.this.tags
}

# ============================================================================
# Windows node pool with autoscaler
# ============================================================================

resource "azurerm_kubernetes_cluster_node_pool" "win" {
  name                  = "win"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.this.id
  vm_size               = var.windows_vm_size
  os_type               = "Windows"
  os_sku                = "Windows2022"
  node_count            = var.windows_min_node_count
  min_count             = var.windows_min_node_count
  max_count             = var.windows_max_node_count
  auto_scaling_enabled  = true
}

# ============================================================================
# Windows Spot node pool with autoscaler (cheaper, can be evicted)
# ============================================================================

resource "azurerm_kubernetes_cluster_node_pool" "winspot" {
  count                 = var.windows_spot_enabled ? 1 : 0
  name                  = "spot"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.this.id
  vm_size               = var.windows_spot_vm_size
  os_type               = "Windows"
  os_sku                = "Windows2022"
  node_count            = var.windows_spot_min_node_count
  min_count             = var.windows_spot_min_node_count
  max_count             = var.windows_spot_max_node_count
  auto_scaling_enabled  = true
  priority              = "Spot"
  eviction_policy       = "Delete"
  spot_max_price        = -1  # pay up to on-demand price

  node_labels = {
    "kubernetes.azure.com/scalesetpriority" = "spot"
  }

  node_taints = [
    "kubernetes.azure.com/scalesetpriority=spot:NoSchedule"
  ]
}

# ============================================================================
# AKS Cluster configuration complete
# ============================================================================

