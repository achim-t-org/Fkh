# Getting Started with Fkh

Fkh lets authorized GitHub users provision Business Central environments on AKS — directly from VS Code or CLI — without Azure credentials.

## Steps

| # | Step | Who | Time |
|---|------|-----|------|
| 1 | [Fork the Repository](docs/01-fork-repository.md) | Ops | 5 min |
| 2 | [Install Prerequisites](docs/02-prerequisites.md) | Ops | 15 min |
| 3 | [Azure Setup](docs/03-azure-setup.md) | Ops | 10 min |
| 4 | [Create the GitHub App](docs/04-github-app.md) | Ops | 10 min |
| 5 | [Configure Your Environment](docs/05-configure-environment.md) | Ops | 10 min |
| 6 | [Deploy with Terraform](docs/06-deploy.md) | Ops | 20 min |
| 7 | [Publish the Fkh Backend](docs/07-publish-function.md) | Ops | 5 min |
| 8 | [Set Up End Users](docs/08-end-user-setup.md) | Users | 5 min |

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
  └── Windows pool: Business Central pods
```

The Fkh backend validates GitHub team membership, then provisions Kubernetes resources using a managed identity. No Azure credentials leave the server.

## What Gets Created

Terraform provisions:

- **Resource Group** — `fkh-<customer>`
- **AKS Cluster** — Linux system pool + Windows autoscale pool
- **Azure Function App** — Consumption plan, .NET 8 isolated worker
- **Container Registry** — Stores BC container images
- **Storage Accounts** — Database backups + Function runtime state
- **Managed Identity** — AKS Contributor + Blob Data Contributor + AcrPull
- **GitHub Teams** — Members team + Admins team
- **Log Analytics + App Insights** — Monitoring and diagnostics
