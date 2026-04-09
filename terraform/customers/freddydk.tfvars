# ── Example customer deployment ───────────────────────────────────────────────
# Copy this file to customers/<customer-name>.tfvars and fill in the values.
# Deploy with: terraform apply -var-file=customers/<customer-name>.tfvars
#
# Never commit github_token or sql_sa_password to source control.
# Use environment variables instead:
#   export TF_VAR_github_token=<token>
#   export TF_VAR_sql_sa_password=<password>

# Azure
subscription_id = "33360653-a61b-4d84-b963-23643b4bd2da"
tenant_id       = "164d3b0c-8dca-45a9-9300-17a0e8bc3325"
location        = "westeurope"
customer_name   = "freddydk"

# AKS
linux_vm_size   = "Standard_D2s_v3"
windows_vm_size = "Standard_D8s_v3"
aks_sku_tier    = "Free"    # Free (dev/test, no SLA) | Standard (99.95% SLA) | Premium (99.99% SLA)
windows_min_node_count = 1  # Set to 1 to keep a warm Windows node
windows_overprovision  = true  # Keep spare capacity (1 CPU + 4Gi) for instant pod scheduling
windows_prepull_images = [  # Images to pre-pull on Windows nodes (speeds up container creation)
  "mcr.microsoft.com/businesscentral:ltsc2022"
]

# SQL Server
# sql_sa_password = ""  # set via TF_VAR_sql_sa_password environment variable
namespace        = "app"
sql_storage_size = "128Gi"

# GitHub token for deployment
# github_token    = ""  # set via TF_VAR_github_token environment variable

# GitHub — primary org for team membership validation
# Note that values here are case sensitive
github_org        = "Freddy-DK"
github_repo       = "Fkh"
github_team_name  = "Fkh-members"
github_team_members = [
  "freddydk"
]
allowed_org_teams = [
  { org = "Freddy-DK",     team = "Fkh-members" },
  { org = "BUNKERHOLDINGBC", team = "Fkh-members" }
]

# Admin teams — members get admin access (and also have normal access)
# Note that values here are case sensitive
github_admin_team_name = "Fkh-admins"
github_admin_team_members = [
  "freddydk"
]
admin_org_teams = [
  { org = "Freddy-DK", team = "Fkh-admins" },
  { org = "BUNKERHOLDINGBC", team = "Fkh-admins" }
]

# Repositories — GitHub repos allowed to call via OIDC from GitHub Actions
# Please note that the AUTH token provided must be the ID token
allowed_oidc_repos = [
  # "Freddy-DK/MyBCApp"
]

# Contact email for Let's Encrypt
contact_email_for_letsencrypt = "fk@freddy.dk"

# GitHub App installed in local fork of Fkh — triggers image-build workflows
github_app_id              = "3279814"  # paste your App ID here
# github_app_private_key   = ""  # set via TF_VAR_github_app_private_key environment variable
github_app_installation_id = "121515383"  # paste your Installation ID here
