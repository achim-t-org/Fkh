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
  description = "Short identifier for the customer. Combined with the FKH prefix in resource names."
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

variable "windows_min_node_count" {
  description = "Minimum number of Windows nodes to keep running (0 = scale to zero, 1 = always keep a warm node)."
  type        = number
  default     = 0
}

variable "windows_prepull_images" {
  description = "List of container images to pre-pull on Windows nodes (e.g. ACR images). Empty list disables pre-pulling."
  type        = list(string)
  default     = []
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
  default     = "Fkh-members"
}

variable "github_repo" {
  description = "GitHub repository name (without org) that is allowed to authenticate via OIDC for image builds."
  type        = string
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

variable "admin_org_teams" {
  description = "List of GitHub org/team pairs that grant admin access. Members also have normal access."
  type = list(object({
    org  = string
    team = string
  }))
  default = []
}

variable "allowed_oidc_repos" {
  description = "List of GitHub repositories (org/repo) allowed to authenticate via OIDC from GitHub Actions workflows."
  type    = list(string)
  default = []
}

variable "github_admin_team_name" {
  description = "Name of the GitHub admin team. Created if it does not exist."
  type        = string
  default     = "Fkh-admins"
}

variable "github_admin_team_members" {
  description = "List of GitHub usernames to add to the admin team."
  type        = list(string)
  default     = []
}

# ── Container image ──────────────────────────────────────────────────────────

variable "base_image" {
  description = "Base Docker image for Business Central container builds."
  type        = string
  default     = "mcr.microsoft.com/businesscentral:ltsc2022"
}

# ── GitHub App (per-customer, triggers image builds) ─────────────────────────

variable "github_app_id" {
  description = "GitHub App ID for triggering image-build workflows."
  type        = string
}

variable "github_app_private_key" {
  description = "PEM-encoded private key of the GitHub App. Set via TF_VAR_github_app_private_key, never in tfvars files."
  type        = string
  sensitive   = true
}

variable "github_app_installation_id" {
  description = "Installation ID of the GitHub App on the target repository."
  type        = string
}

variable "contact_email_for_letsencrypt" {
  description = "Contact email for Let's Encrypt certificate generation."
  type        = string
}
