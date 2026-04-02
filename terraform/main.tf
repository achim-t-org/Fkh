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
  product_prefix              = "fk8s"
  storage_account_customer_id = substr(replace(var.customer_name, "-", ""), 0, 14)

  resource_group_name    = "${local.product_prefix}-${var.customer_name}"
  aks_cluster_name       = "${local.product_prefix}-${var.customer_name}-aks"
  aks_dns_prefix         = local.aks_cluster_name
  function_plan_name     = "${local.product_prefix}-${var.customer_name}-plan"
  function_app_name      = "${local.product_prefix}-${var.customer_name}-provisioner"
  function_identity_name = "${local.product_prefix}-${var.customer_name}-identity"
  function_storage_name  = "${local.product_prefix}${local.storage_account_customer_id}func"
}

# ============================================================================
# Resource Group
# ============================================================================

resource "azurerm_resource_group" "this" {
  name     = local.resource_group_name
  location = var.location

  tags = {
    customer    = var.customer_name
    environment = var.environment
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
  node_count            = 0
  min_count             = 0
  max_count             = 10
  auto_scaling_enabled  = true
}

# ============================================================================
# Configure Kubernetes provider using AKS credentials
# ============================================================================

provider "kubernetes" {
  host                   = azurerm_kubernetes_cluster.this.kube_config[0].host
  client_certificate     = base64decode(azurerm_kubernetes_cluster.this.kube_config[0].client_certificate)
  client_key             = base64decode(azurerm_kubernetes_cluster.this.kube_config[0].client_key)
  cluster_ca_certificate = base64decode(azurerm_kubernetes_cluster.this.kube_config[0].cluster_ca_certificate)
}
