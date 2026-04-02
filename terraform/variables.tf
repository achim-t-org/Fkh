# ── Azure ─────────────────────────────────────────────────────────────────────

variable "subscription_id" {
  description = "The Azure subscription ID to deploy into."
  type        = string
}

variable "tenant_id" {
  description = "The Azure AD tenant ID."
  type        = string
}

variable "location" {
  description = "Azure region for all resources."
  type        = string
  default     = "westeurope"
}

variable "customer_name" {
  description = "Short identifier for the customer. Combined with the FK8s prefix in resource names."
  type        = string
}

# ── AKS ───────────────────────────────────────────────────────────────────────

variable "linux_vm_size" {
  description = "VM size for the Linux node pool."
  type        = string
  default     = "Standard_D2s_v3"
}

variable "windows_vm_size" {
  description = "VM size for the Windows node pool."
  type        = string
  default     = "Standard_D2s_v3"
}

variable "aks_sku_tier" {
  description = "AKS control plane tier. 'Free' for dev/test (no SLA). 'Standard' for production (99.95% SLA, ~$73/month)."
  type        = string
  default     = "Free"

  validation {
    condition     = contains(["Free", "Standard", "Premium"], var.aks_sku_tier)
    error_message = "aks_sku_tier must be one of: Free, Standard, Premium."
  }
}

# ── SQL Server ────────────────────────────────────────────────────────────────

variable "sql_sa_password" {
  description = "The SA password for SQL Server. Must be at least 8 characters."
  type        = string
  sensitive   = true

  validation {
    condition     = length(var.sql_sa_password) >= 8
    error_message = "SQL SA password must be at least 8 characters long."
  }
}

variable "namespace" {
  description = "Kubernetes namespace for the workload."
  type        = string
  default     = "app"
}

variable "sql_storage_size" {
  description = "Storage size for the SQL Server PVC."
  type        = string
  default     = "128Gi"
}

# ── GitHub ────────────────────────────────────────────────────────────────────

variable "github_org" {
  description = "The GitHub organization where the access team will be created."
  type        = string
}

variable "github_token" {
  description = "GitHub personal access token with admin:org scope. Set via TF_VAR_github_token, never in tfvars files."
  type        = string
  sensitive   = true
}

variable "github_team_name" {
  description = "Name of the GitHub team that controls access to the provisioner. Created if it does not exist."
  type        = string
  default     = "FK8s-members"
}

variable "github_team_members" {
  description = "List of GitHub usernames to add to the access team."
  type        = list(string)
  default     = []
}

# ── Function access config ────────────────────────────────────────────────────

variable "allowed_org_teams" {
  description = "List of GitHub org/team pairs the Azure Function will accept."
  type = list(object({
    org  = string
    team = string
  }))
}
