# ── Example organization deployment ─────────────────────────────────────────────────────────
# Copy this file to organizations/<org-name>.tfvars and fill in the values.
# Deploy with: .\deploy.ps1 -VarFile organizations/<org-name>.tfvars
#
# Never commit secrets (github_token, sql_sa_password, github_app_private_key) to source control.
# Use environment variables instead:
#   $env:TF_VAR_github_token = "<token>"
#   $env:TF_VAR_sql_sa_password = "<password>"
#   $env:TF_VAR_github_app_private_key = Get-Content "<path-to>.pem" -Raw

# Azure
subscription_id = "00000000-0000-0000-0000-000000000000"
tenant_id       = "00000000-0000-0000-0000-000000000000"
location        = "westeurope"
org_name   = "my-org"

# AKS
aks_sku_tier    = "Free"    # Free (dev/test, no SLA) | Standard (99.95% SLA) | Premium (99.99% SLA)
linux_vm_size   = "Standard_D4s_v5"    # v6 not supported for sqlserver
windows_vm_size = "Standard_D4s_v5"    # v6 not supported for hyhervisor gen1
windows_min_node_count = 0  # Set to 1 to keep a warm Windows node (~$70-100/mo)
windows_max_node_count = 10 # Maximum Windows nodes the autoscaler can scale to
windows_overprovision        = false   # Set to true to keep spare capacity for instant container scheduling
windows_overprovision_cpu    = "250m"  # CPU reserved by the overprovision placeholder
windows_overprovision_memory = "3Gi"   # Memory reserved by the overprovision placeholder
container_default_cpu        = "250m"  # Default CPU request for BC containers
container_default_memory     = "3Gi"   # Default memory request for BC containers
windows_spot_enabled        = false  # Set to true to add a Spot pricing Windows pool for lower cost
windows_spot_vm_size        = "Standard_D2ds_v5"  # VM size for spot nodes
windows_spot_min_node_count = 0      # Minimum spot nodes (0 = scale to zero when idle)
windows_spot_max_node_count = 10     # Maximum spot nodes the autoscaler can scale to
windows_prepull_images = [  # Images to pre-pull on Windows nodes (speeds up container creation)
  # "<acr-name>.azurecr.io/businesscentral:<tag>"
]

# aad_app_client_id = "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"    # Add AAD App Client Id for WebClient AAD App to support AAD Authentication in containers


# SQL Server
# sql_sa_password = ""  # set via TF_VAR_sql_sa_password environment variable
namespace        = "app"
sql_storage_size = "128Gi"

# GitHub token for deployment
# github_token    = ""  # set via TF_VAR_github_token environment variable

# GitHub — primary org for team membership validation
# Note that values here are case sensitive
github_org        = "my-company"
github_repo       = "Fkh"             # your fork name
github_team_name  = "Fkh-members"
github_team_members = [
  "freddyk"
]
allowed_org_teams = [
  { org = "my-company",     team = "Fkh-members" },
  { org = "partner-org",   team = "Fkh-members" }
]

# Admin teams — members get admin access (and also have normal access)
# Note that values here are case sensitive
github_admin_team_name = "Fkh-admins"
github_admin_team_members = [
  # "admin-username"
]
admin_org_teams = [
  { org = "my-company",     team = "Fkh-admins" }
]

# Repositories — GitHub repos allowed to call via OIDC from GitHub Actions
# Please note that the AUTH token provided must be the ID token
allowed_oidc_repos = [
  # "my-company/my-bc-app"
]


# Contact email for Let's Encrypt
contact_email_for_letsencrypt = "admin@example.com"

# GitHub App installed in local fork of Fkh — triggers image-build workflows
github_app_id              = ""  # paste your App ID here
# github_app_private_key   = ""  # set via TF_VAR_github_app_private_key environment variable
github_app_installation_id = ""  # paste your Installation ID here

# Default user settings (deployed to settings/usersettings.json in storage)
# _members = defaults for all users, _admins = defaults for admin users
default_user_settings = <<-EOT
  {
    "_members": {
      "MaxContainers": 3
    },
    "_admins": {
      "MaxContainers": 10
    }
  }
EOT

# Kubecost — free per-pod cost analysis dashboard
# Needs minimum D4s Linux VM Size to enable
kubecost_enabled = false
