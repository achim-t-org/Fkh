# Azure Setup (Path B — Local Deployment)

## Required Permissions

The person deploying needs these Azure permissions:

| Permission | Scope | Why |
|-----------|-------|-----|
| **Contributor** | Subscription | Create resource groups, AKS, Function App, storage, ACR |
| **User Access Administrator** | Subscription | Assign roles to the managed identity |

> If your org uses custom roles, you need: create/manage resource groups, AKS clusters, Function Apps, storage accounts, container registries, managed identities, role assignments, and Log Analytics workspaces.

## Collect These Values

You'll need these for the tfvars file:

| Value | Where to find it |
|-------|-----------------|
| **Subscription ID** | Azure Portal → Subscriptions, or `az account show --query id -o tsv` |
| **Tenant ID** | Azure Portal → Microsoft Entra ID → Overview, or `az account show --query tenantId -o tsv` |
| **Region** | Pick one: `westeurope`, `eastus`, `swedencentral`, etc. |

## Login to Azure

```powershell
az login
az account list --output table
az account set --subscription "<your-subscription-id>"
```

## Next Step

→ [Create the GitHub App](GitHubApp.md)
