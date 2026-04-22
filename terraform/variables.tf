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

variable "org_name" {
  description = "Short identifier for the organization. Combined with the FKH prefix in resource names."
  type        = string
}

# ── AKS ───────────────────────────────────────────────────────────────────────

variable "linux_vm_size" {
  description = "VM size for the Linux node pool. Use v5 series (v6 is not yet supported by AKS Windows nodes and SQL Server images)."
  type        = string
  default     = "Standard_D4s_v5"
}

variable "windows_vm_size" {
  description = "VM size for the Windows node pool. Use v5 series (v6 is not yet supported by AKS Windows nodes and SQL Server images)."
  type        = string
  default     = "Standard_D4s_v5"
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

variable "windows_max_node_count" {
  description = "Maximum number of Windows nodes the autoscaler can scale to."
  type        = number
  default     = 10
}

variable "windows_spot_enabled" {
  description = "Whether to create a Windows Spot node pool for cheaper, interruptible workloads."
  type        = bool
  default     = false
}

variable "windows_spot_vm_size" {
  description = "VM size for the Windows Spot node pool. Use v5 series (v6 is not yet supported by AKS Windows nodes and SQL Server images)."
  type        = string
  default     = "Standard_D8s_v5"
}

variable "windows_spot_min_node_count" {
  description = "Minimum number of Windows Spot nodes (0 = scale to zero)."
  type        = number
  default     = 0
}

variable "windows_spot_max_node_count" {
  description = "Maximum number of Windows Spot nodes the autoscaler can scale to."
  type        = number
  default     = 10
}

variable "windows_overprovision" {
  description = "Keep a low-priority placeholder pod on Windows VMs to reserve spare capacity. When a real container is created it preempts the placeholder instantly, and the autoscaler provisions a new VM in the background."
  type        = bool
  default     = false
}

variable "windows_overprovision_cpu" {
  description = "CPU request for the overprovision placeholder pod (e.g. '250m', '500m')."
  type        = string
  default     = "250m"
}

variable "windows_overprovision_memory" {
  description = "Memory request for the overprovision placeholder pod (e.g. '3Gi', '6Gi')."
  type        = string
  default     = "3Gi"
}

variable "container_default_cpu" {
  description = "Default CPU request for BC containers when not specified by the user (e.g. '250m', '500m')."
  type        = string
  default     = "250m"
}

variable "container_default_memory" {
  description = "Default memory request for BC containers when not specified by the user (e.g. '3Gi', '4Gi')."
  type        = string
  default     = "3Gi"
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

# ── GitHub App (per-organization, triggers image builds) ─────────────────────────

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

# ── User Settings ─────────────────────────────────────────────────────────────

variable "default_user_settings" {
  description = "Default user settings JSON. Deployed to the 'settings/defaultusersettings.json' blob. Keys '_members' and '_admins' define defaults for each role."
  type        = string
  default     = <<-EOT
    {
      "_members": {
        "MaxContainers": 3
      },
      "_admins": {
        "MaxContainers": 10
      }
    }
  EOT
}

variable "kubecost_enabled" {
  description = "Deploy Kubecost free tier for per-pod cost analysis."
  type        = bool
  default     = false
}
