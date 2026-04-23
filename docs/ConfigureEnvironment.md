# Configure Your Environment

## GitHub Token (Path B only)

> Skip this section if you're using **Path A** (GitHub Actions). The GitHub App handles authentication.

The deploy script no longer requires a GitHub PAT. Authentication for GitHub API calls is handled by the GitHub App configured in Step 3.

## Create Your Organization tfvars File

```powershell
Copy-Item deployment-repo/config/deployment.tfvars terraform/organizations/<your-name>.tfvars
```

Edit the file and fill in all values:

```hcl
subscription_id = "00000000-0000-0000-0000-000000000000"
tenant_id       = "00000000-0000-0000-0000-000000000000"
location        = "westeurope"
fkhDeploymentName = "my-deployment"

# AKS
aks_sku_tier    = "Free"    # Free (dev/test, no SLA) | Standard (99.95% SLA) | Premium (99.99% SLA)
linux_vm_size   = "Standard_D4s_v5"    # v6 not supported for sqlserver
windows_vm_size = "Standard_D4s_v5"    # v6 not supported for hypervisor gen1
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

# SQL Server
# sql_sa_password = ""  # set via TF_VAR_sql_sa_password environment variable
namespace        = "app"
sql_storage_size = "128Gi"

# GitHub — primary org for team membership validation
# Note that values here are case sensitive
github_org        = "my-company"
github_team_name  = "Fkh-members"
github_team_members = [
  "user1"
]
allowed_org_teams = [
  { org = "my-company",   team = "Fkh-members" },
  { org = "partner-org",  team = "Fkh-members" }
]

# Admin teams — members get admin access (and also have normal access)
# Note that values here are case sensitive
github_admin_team_name = "Fkh-admins"
github_admin_team_members = [
  # "admin-username"
]
admin_org_teams = [
  { org = "my-company",   team = "Fkh-admins" }
]

# Repositories — GitHub repos allowed to call via OIDC from GitHub Actions
# Please note that the AUTH token provided must be the ID token
allowed_oidc_repos = [
  # "my-company/my-bc-app"
]

# Contact email for Let's Encrypt
contact_email_for_letsencrypt = "admin@example.com"

# GitHub App (see docs/GitHubApp.md)
github_app_id              = ""   # App ID from the GitHub App settings page
# github_app_private_key   = ""  # set via TF_VAR_github_app_private_key environment variable
github_app_installation_id = ""   # Installation ID from the install URL
create_images_repo         = "my-company/Fkh"  # org/repo where Create Images workflow runs

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
```

For **Path A**: upload the tfvars file to a secure location (e.g. Azure Blob Storage with a SAS URL) and add the download URL as a `TFVARS_URL` GitHub secret (Settings → Secrets and variables → Actions → Secrets → New secret). The workflow downloads the file at deploy time and masks all values in logs. Alternatively, commit the file to the repo and set the `TFVARS_FILE` variable to its path instead.

For **Path B**: the file stays local — `deploy.ps1` reads it directly.

## Set Secrets as Environment Variables (Path B only)

> Skip this section if you're using **Path A** (GitHub Actions). These values are configured as GitHub secrets instead — see [Prerequisites (Path A)](Prerequisites-PathA.md#github-secrets).

**Never put these in tfvars files.**

```powershell
# SQL Server SA password (8+ chars, mix of upper/lower/numbers/symbols)
$env:TF_VAR_sql_sa_password = "<strong-password>"

# GitHub App private key
$env:TF_VAR_github_app_private_key = Get-Content "<path-to>.pem" -Raw
```

> The deploy script will prompt for these if not set, and will recover them from Terraform state on redeployments.

## Values Checklist

| Value | Source | In tfvars? | Path A (Actions) | Path B (Local) |
|-------|--------|-----------|-----------------|----------------|
| Subscription ID | Azure Portal / `az account show` | ✅ | ✅ | ✅ |
| Tenant ID | Azure Portal / `az account show` | ✅ | ✅ | ✅ |
| Organization name | You choose | ✅ | ✅ | ✅ |
| GitHub org | Your GitHub org | ✅ | ✅ | ✅ |
| GitHub team members | Usernames | ✅ | ✅ | ✅ |
| GitHub App ID | Create the GitHub App | ✅ | ✅ | ✅ |
| GitHub App Installation ID | Create the GitHub App | ✅ | ✅ | ✅ |
| SQL SA password | You choose | ❌ | `SQL_SA_PASSWORD` secret | env var |
| GitHub App private key | Create the GitHub App (.pem) | ❌ | `GH_APP_PRIVATE_KEY` secret | env var |
| Azure App Registration | Azure Portal (OIDC) | ❌ | `AZURE_DEPLOY_CLIENT_ID` secret | not needed |
