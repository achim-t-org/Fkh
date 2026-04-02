# ── Example customer deployment ───────────────────────────────────────────────
# Copy this file to customers/<customer-name>.tfvars and fill in the values.
# Deploy with: terraform apply -var-file=customers/<customer-name>.tfvars
#
# Never commit github_token or sql_sa_password to source control.
# Use environment variables instead:
#   export TF_VAR_github_token=<token>
#   export TF_VAR_sql_sa_password=<password>

# Azure
subscription_id = "00000000-0000-0000-0000-000000000000"
tenant_id       = "00000000-0000-0000-0000-000000000000"
location        = "westeurope"
customer_name   = "customer-a"
environment     = "prod"

# AKS
linux_vm_size   = "Standard_D2s_v3"
windows_vm_size = "Standard_D2s_v3"
aks_sku_tier    = "Free"    # Free (dev/test, no SLA) | Standard (99.95% SLA) | Premium (99.99% SLA)

# SQL Server
# sql_sa_password = ""  # set via TF_VAR_sql_sa_password environment variable
namespace        = "app"
sql_storage_size = "128Gi"

# GitHub — primary org for team membership validation
github_org        = "my-company"
# github_token    = ""  # set via TF_VAR_github_token environment variable
github_team_name  = "FK8s-members"
github_team_members = [
  "freddyk"
]

# Orgs and teams the Azure Function will accept
allowed_org_teams = [
  { org = "my-company",     team = "FK8s-members" },
  { org = "customer-a-org", team = "FK8s-members" }
]
