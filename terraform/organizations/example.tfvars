# ── Example organization deployment ─────────────────────────────────────────────────────────
# Copy this file to organizations/<org-name>.tfvars and fill in the values.
# Deploy with: terraform apply -var-file=organizations/<org-name>.tfvars
#
# Never commit github_token or sql_sa_password to source control.
# Use environment variables instead:
#   export TF_VAR_github_token=<token>
#   export TF_VAR_sql_sa_password=<password>

# Azure
subscription_id = "00000000-0000-0000-0000-000000000000"
tenant_id       = "00000000-0000-0000-0000-000000000000"
location        = "westeurope"
org_name   = "my-org"

# AKS
aks_sku_tier    = "Free"    # Free (dev/test, no SLA) | Standard (99.95% SLA) | Premium (99.99% SLA)
linux_vm_size   = "Standard_D2s_v3"
windows_vm_size = "Standard_D2s_v3"
windows_min_node_count = 0  # Set to 1 to keep a warm Windows node (~$70-100/mo)
windows_max_node_count = 10 # Maximum Windows nodes the autoscaler can scale to
windows_overprovision  = false  # Set to true to keep spare capacity for instant pod scheduling
windows_spot_enabled        = false  # Set to true to add a Spot pricing Windows pool for lower cost
windows_spot_vm_size        = "Standard_D2ds_v5"  # VM size for spot nodes
windows_spot_min_node_count = 0      # Minimum spot nodes (0 = scale to zero when idle)
windows_spot_max_node_count = 10     # Maximum spot nodes the autoscaler can scale to
windows_prepull_images = [  # Images to pre-pull on Windows nodes (speeds up container creation)
  # "<acr-name>.azurecr.io/businesscentral:<tag>"
]

# SQL Server
# sql_sa_password = ""  # set via TF_VAR_sql_sa_password environment variable
namespace        = "app"
sql_storage_size = "128Gi"

# GitHub token for deployment
# github_token    = ""  # set via TF_VAR_github_token environment variable

# GitHub — primary org for team membership validation
# Note that values here are case sensitive
github_org        = "my-company"
github_team_name  = "FKH-members"
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
