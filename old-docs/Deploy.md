# Deploy with Terraform

> This page covers **Path B** (local deployment). If you're using **Path A** (GitHub Actions), just run the **Deploy** workflow from your repo's Actions tab — it handles all of the below automatically.

## Deploy

Use the deploy script — it handles Terraform init, state storage, GitHub team checks, and the full apply:

```powershell
cd terraform
.\deploy.ps1 -VarFile organizations/<your-name>.tfvars
```

To skip the interactive confirmation prompt:

```powershell
.\deploy.ps1 -VarFile organizations/<your-name>.tfvars -AutoApprove
```

Deployment takes ~15–20 minutes (AKS cluster creation is the slowest part).

The script automatically:
1. Creates Terraform state storage in Azure
2. Runs `terraform init` with the correct backend config
3. Checks and imports existing GitHub teams
4. Bootstraps core infrastructure (AKS, ACR, Function App)
5. Runs the full `terraform apply`
6. Publishes the backend function code
7. Syncs GitHub Actions secrets (if `gh` CLI is available)

## What Gets Created

| Resource | Name Pattern | Purpose |
|----------|-------------|---------|
| Resource Group | `fkh-<org>` | Contains everything |
| AKS Cluster | `fkh-<org>-aks` | Linux + Windows node pools |
| Function App | `fkh-<org>-backend` | Auth gate + provisioning API |
| Container Registry | `fkh<org>acr` | BC container images |
| Storage (DBS) | `fkh<org>dbs` | Database backups |
| Storage (Func) | `fkh<org>func` | Function runtime state |
| Managed Identity | `fkh-<org>-identity` | Azure auth for the Function |
| Log Analytics | `fkh-<org>-logs` | Monitoring |
| App Insights | `fkh-<org>-insights` | Function telemetry |
| GitHub Teams | `Fkh-members`, `Fkh-admins` | Access control |

## Verify

After deploy, check the outputs:

```powershell
terraform output
```

Key outputs:
- `function_app_name` — the Function App name
- `function_url` — the base URL for the VSIX / CLI configuration

## Subsequent Deployments

After the first deploy, just run:

```powershell
.\deploy.ps1 -VarFile organizations/<your-name>.tfvars
```

Only changed resources are updated. Adding team members, changing VM sizes, etc. are all incremental.
