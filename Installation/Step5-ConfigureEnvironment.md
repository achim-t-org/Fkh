# Step 5 — Configure Your Environment

> **Performed by:** GitHub Organization Admin (with values collected from earlier steps)

In this step you fill out the `deployment.tfvars` configuration file and create the GitHub Secrets that the deployment workflow needs. Everything is done in the **private deployment repository** you created in Step 1.

---

## 5.1 — Edit deployment.tfvars

Open `config/deployment.tfvars` in your deployment repository. Walk through each section below and fill in the values.

### Deployment name

```hcl
fkhDeploymentName = "contoso"
```

A short identifier for this deployment. Used as a prefix for all Azure resource names (e.g. `fkh-contoso-aks`, `fkh-contoso-backend`). Use only lowercase letters, numbers, and hyphens.

**Where it comes from:** You choose this. It should identify the deployment (e.g. your company name or project).

### Azure settings

```hcl
subscription_id = "00000000-0000-0000-0000-000000000000"
tenant_id       = "00000000-0000-0000-0000-000000000000"
location        = "westeurope"
```

| Setting | Where to find it |
|---------|-----------------|
| `subscription_id` | Azure Portal → **Subscriptions** → copy the **Subscription ID**, or run `az account show --query id -o tsv` |
| `tenant_id` | Azure Portal → **Microsoft Entra ID** → **Overview** → **Tenant ID**, or run `az account show --query tenantId -o tsv` |
| `location` | The Azure region where all resources will be created. Common values: `westeurope`, `eastus`, `swedencentral`. Pick one close to your users. |

### Kubernetes settings

These have sensible defaults — you can leave them as-is for a first deployment and tune later:

| Setting | Default | Notes |
|---------|---------|-------|
| `aks_sku_tier` | `"Free"` | `Free` = no SLA (dev/test), `Standard` = 99.95% SLA, `Premium` = 99.99% SLA |
| `linux_vm_size` | `"Standard_D4s_v5"` | Linux system node pool. D4s minimum recommended for SQL Server |
| `windows_vm_size` | `"Standard_D4s_v5"` | Windows node pool for BC containers |
| `windows_min_node_count` | `0` | Set to `1` to keep a warm node (~$70–100/mo). `0` = scale to zero when idle |
| `windows_max_node_count` | `10` | Upper limit for the autoscaler |

> **Cost tip:** With `windows_min_node_count = 0`, you only pay for Windows nodes when containers are running. The first container takes longer to start because a node must be provisioned.

The spot pool, overprovision, and prepull settings are all optional optimizations — skip them for now.

### SQL Server

```hcl
namespace        = "app"
sql_storage_size = "128Gi"
```

Leave these at their defaults unless you have a specific reason to change them. The SA password is set as a GitHub Secret (see below), not in this file.

### Contact email

```hcl
contact_email_for_letsencrypt = "admin@example.com"
```

Used by Let's Encrypt for certificate expiry notifications. Replace with a real email address.

### AAD container authentication (optional)

```hcl
enable_aad_container_auth = false
```

Set to `true` if you want users to sign into BC containers with their Microsoft 365 accounts. Requires the deployment identity to have the **Privileged Role Administrator** directory role in Entra ID (see Step 2, optional step A.5 or B.4).

### GitHub teams (authorization)

These reference the teams you created in Step 4. Values are **case-sensitive** and must match exactly.

```hcl
allowed_org_teams = [
  { org = "my-company", team = "Fkh-members" }
]

admin_org_teams = [
  { org = "my-company", team = "Fkh-admins" }
]
```

| Setting | What it controls |
|---------|-----------------|
| `allowed_org_teams` | Teams whose members can provision BC containers |
| `admin_org_teams` | Teams whose members get admin access (they also have normal access) |

### OIDC repos (optional)

```hcl
allowed_oidc_repos = [
  # "my-company/my-bc-app"
]
```

GitHub repositories that can authenticate to the Fkh Function App via OIDC (e.g. for CI/CD pipelines that create containers). Leave empty unless you have this use case.

### GitHub App

```hcl
github_app_id              = "123456"
github_app_installation_id = "12345678"
```

| Setting | Where to find it |
|---------|-----------------|
| `github_app_id` | From Step 3.4 — GitHub App settings page, near the top |
| `github_app_installation_id` | From Step 3.4 — the number in the installation URL |

The private key is **not** stored here — it goes in a GitHub Secret (see below).

### User settings (optional)

```hcl
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
```

Controls defaults like how many simultaneous containers each user can have. Adjust the numbers to fit your environment.

---

## 5.2 — Commit deployment.tfvars

Commit and push the updated `config/deployment.tfvars` to the `main` branch of your deployment repository. This file contains no secrets — all sensitive values go into GitHub Secrets.

---

## 5.3 — Create GitHub Secrets

Go to your **deployment repository** → **Settings** → **Secrets and variables** → **Actions** → **New repository secret**.

Create each of the following secrets:

### `AZURE_DEPLOY_CLIENT_ID`

The **Client ID** of the deployment identity you created in Step 2.

| Identity type | Where to find it |
|--------------|-----------------|
| Managed Identity | Azure Portal → the Managed Identity → **Overview** → **Client ID** |
| App Registration | Azure Portal → **App registrations** → your app → **Overview** → **Application (client) ID** |

> **Note:** `tenant_id` and `subscription_id` no longer need to be set as secrets — they are read from `config/deployment.tfvars` automatically.

### `SQL_SA_PASSWORD`

The **SA password** for the SQL Server that runs in AKS.

**Where it comes from:** You choose this. It must be at least 8 characters and meet SQL Server complexity requirements (uppercase, lowercase, number or special character).

> **Important:** Once deployed, changing this password requires manual intervention. Pick a strong password and store it in a password manager.

### `GH_APP_PRIVATE_KEY`

The **PEM-encoded private key** of the GitHub App you created in Step 3.

**Where to find it:** The `.pem` file downloaded in Step 3.2. Paste the entire file contents, including the `-----BEGIN RSA PRIVATE KEY-----` and `-----END RSA PRIVATE KEY-----` lines.

---

## Summary

After completing this step, your deployment repository should have:

| What | Where | Count |
|------|-------|-------|
| Configuration file | `config/deployment.tfvars` (committed) | All non-secret settings |
| `AZURE_DEPLOY_CLIENT_ID` | GitHub Secret | From Step 2 |
| `SQL_SA_PASSWORD` | GitHub Secret | You choose |
| `GH_APP_PRIVATE_KEY` | GitHub Secret | From Step 3 |

You are now ready to deploy.

---

*Previous: [Step 4 — Set Up GitHub Teams](Step4-GitHubTeams.md)*
*Next: [Step 6 — Deploy](Step6-Deploy.md)*
