# Getting Started with Fkh

Fkh lets authorized GitHub users provision Business Central environments on AKS — directly from VS Code or CLI — without Azure credentials.

## Choose Your Deployment Method

### Path A: Deploy from GitHub Actions (recommended)

No local tools needed. Everything runs in the cloud.

| # | Step | Who | Time |
|---|------|-----|------|
| A1 | [Fork the Repository](docs/ForkRepository.md) | Ops | 5 min |
| A2 | [Collect Azure Info + Set Up OIDC](docs/AzureSetup-PathA.md) | Ops | 10 min |
| A3 | [Create the GitHub App](docs/GitHubApp.md) | Ops | 10 min |
| A4 | [Configure Your Environment](docs/ConfigureEnvironment.md) | Ops | 10 min |
| A5 | [Configure GitHub Secrets](docs/Prerequisites-PathA.md#github-secrets) | Ops | 5 min |
| A6 | Run the **Deploy** workflow (Actions → Deploy → Run workflow) | Ops | 20 min |
| A7 | [Set Up End Users](docs/EndUserSetup.md) | Users | 5 min |

### Path B: Deploy from your own machine

Full local control with CLI tools.

| # | Step | Who | Time |
|---|------|-----|------|
| B1 | [Fork the Repository](docs/ForkRepository.md) | Ops | 5 min |
| B2 | [Install Prerequisites](docs/Prerequisites-PathB.md) | Ops | 15 min |
| B3 | [Azure Setup & Login](docs/AzureSetup-PathB.md) | Ops | 10 min |
| B4 | [Create the GitHub App](docs/GitHubApp.md) | Ops | 10 min |
| B5 | [Configure Your Environment](docs/ConfigureEnvironment.md) | Ops | 10 min |
| B6 | [Deploy with Terraform](docs/Deploy.md) | Ops | 20 min |
| B7 | [Publish the Fkh Backend](docs/PublishFunction.md) | Ops | 5 min |
| B8 | [Set Up End Users](docs/EndUserSetup.md) | Users | 5 min |

> **Tip:** After the initial setup, use the **Deploy Function Update** workflow (or `deploy-functionupdate.ps1`) to quickly publish backend code changes without re-running the full infrastructure deploy.

## Architecture Overview

```
User (VS Code / CLI)
  │
  ▼  POST /api/* (GitHub Bearer token)
Fkh backend (auth gate)
  │
  ▼  Managed Identity
AKS Cluster
  ├── Linux pool: SQL Server
  ├── Windows pool: Business Central containers
  └── Windows Spot pool (optional): lower-cost BC containers (can be evicted)
```

The Fkh backend validates GitHub team membership, then provisions Kubernetes resources using a managed identity. No Azure credentials leave the server.

## What Gets Created

Terraform provisions:

- **Resource Group** — `fkh-<org>`
- **AKS Cluster** — Linux system pool + Windows autoscale pool + optional Windows Spot pool (cheaper, preemptible)
- **Azure Function App** — Consumption plan, .NET 8 isolated worker
- **Container Registry** — Stores BC container images
- **Storage Accounts** — Database backups + Function runtime state
- **Managed Identity** — AKS Contributor + Blob Data Contributor + AcrPull
- **GitHub Teams** — Members team + Admins team
- **Log Analytics + App Insights** — Monitoring and diagnostics
