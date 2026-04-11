# Configure Your Environment

## GitHub Token (Path B only)

> Skip this section if you're using **Path A** (GitHub Actions). The `GH_PAT` secret handles this.

The deploy script automatically reads your GitHub token from the GitHub CLI (`gh auth token`). Just make sure you're logged in:

```powershell
gh auth login
```

When prompted, select scopes that include `admin:org` and `read:user`:

| Scope | Why |
|-------|-----|
| `admin:org` | Terraform creates and manages GitHub teams |
| `read:user` | Validate user identity |

> Alternatively, you can set `TF_VAR_github_token` to a classic PAT with these scopes. Fine-grained tokens won't work — Terraform's GitHub provider requires classic token scopes.

## Create Your Organization tfvars File

```powershell
Copy-Item terraform/organizations/example.tfvars terraform/organizations/<your-name>.tfvars
```

Edit the file and fill in all values:

```hcl
# Azure
subscription_id = "<from Azure Portal or az account show>"
tenant_id       = "<from Azure Portal or az account show>"
location        = "westeurope"
org_name   = "mycompany"         # lowercase, no spaces

# AKS
linux_vm_size   = "Standard_D2s_v3"   # system pool, always on
windows_vm_size = "Standard_D8s_v3"   # BC containers run here
aks_sku_tier    = "Free"              # Free | Standard ($73/mo SLA)
windows_min_node_count = 0            # 0 = scale to zero, 1 = warm node
windows_overprovision  = false
windows_prepull_images = []

# SQL Server
namespace        = "app"
sql_storage_size = "128Gi"

# GitHub
github_org        = "your-org"        # case sensitive
github_repo       = "Fkh"            # your fork name
github_team_name  = "Fkh-members"
github_team_members = [
  "user1",
  "user2"
]
allowed_org_teams = [
  { org = "your-org", team = "Fkh-members" }
]

# Admin team
github_admin_team_name = "Fkh-admins"
github_admin_team_members = [
  "admin-user"
]
admin_org_teams = [
  { org = "your-org", team = "Fkh-admins" }
]

# OIDC (repos allowed to call via GitHub Actions)
allowed_oidc_repos = [
  # "your-org/your-bc-app"
]

# Let's Encrypt
contact_email_for_letsencrypt = "admin@example.com"

# GitHub App (from Create the GitHub App step)
github_app_id              = "<app-id>"
github_app_installation_id = "<installation-id>"
```

For **Path A**: commit and push the tfvars file to your repository so the Deploy workflow can find it.

For **Path B**: the file stays local — `deploy.ps1` reads it directly.

## Set Secrets as Environment Variables (Path B only)

> Skip this section if you're using **Path A** (GitHub Actions). These values are configured as GitHub secrets instead — see [Prerequisites: Method 2](Prerequisites.md#method-2-deploy-from-github-actions-recommended).

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
| GitHub PAT | `gh auth login` or classic PAT | ❌ | `GH_PAT` secret | automatic via `gh` |
| SQL SA password | You choose | ❌ | `SQL_SA_PASSWORD` secret | env var |
| GitHub App private key | Create the GitHub App (.pem) | ❌ | `GITHUB_APP_PRIVATE_KEY` secret | env var |
| Azure App Registration | Azure Portal (OIDC) | ❌ | `AZURE_CLIENT_ID` secret | not needed |
