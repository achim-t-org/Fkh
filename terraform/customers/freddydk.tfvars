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
windows_vm_size = "Standard_D2s_v3"
aks_sku_tier    = "Free"    # Free (dev/test, no SLA) | Standard (99.95% SLA) | Premium (99.99% SLA)

# SQL Server
# sql_sa_password = ""  # set via TF_VAR_sql_sa_password environment variable
namespace        = "app"
sql_storage_size = "128Gi"

# GitHub — primary org for team membership validation
github_org        = "Freddy-DK"
# github_token    = ""  # set via TF_VAR_github_token environment variable
github_team_name  = "FK8s-members"
github_team_members = [
  "freddydk"
]

# Orgs and teams the Azure Function will accept
allowed_org_teams = [
  { org = "Freddy-DK",     team = "FK8s-members" }
]
