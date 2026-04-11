# Prerequisites — Path A (GitHub Actions)

No local tools required. The GitHub workflows handle everything.

You'll need to set up Azure OIDC and configure GitHub secrets before running the workflows. See [Azure Setup (Path A)](AzureSetup-PathA.md) for the OIDC steps, then come back here to configure the secrets.

## GitHub Secrets

Go to your fork's **Settings → Secrets and variables → Actions** and add these secrets:

| Secret | Value |
|--------|-------|
| `AZURE_DEPLOY_CLIENT_ID` | App Registration's Application (client) ID |
| `AZURE_DEPLOY_TENANT_ID` | Azure AD tenant ID |
| `AZURE_DEPLOY_SUBSCRIPTION_ID` | Target Azure subscription ID |
| `SQL_SA_PASSWORD` | SA password for the SQL Server in AKS (min 8 chars) |
| `GH_APP_PRIVATE_KEY` | PEM-encoded private key of the GitHub App (from [Create the GitHub App](GitHubApp.md)) |
| `GH_PAT` | GitHub PAT with scopes: `admin:org`, `repo`, `read:org` |

## GitHub Variable

Go to **Settings → Secrets and variables → Actions → Variables** and add:

| Variable | Value |
|----------|-------|
| `TFVARS_FILE` | Path to your `.tfvars` file, e.g. `organizations/my-org.tfvars` |

## For End Users (VS Code only)

Just VS Code with the Fkh extension. Nothing else needed.

```powershell
winget install Microsoft.VisualStudioCode
```

## For End Users (CLI)

```powershell
winget install Microsoft.DotNet.SDK.8
winget install GitHub.cli
gh auth login
```

## For Extension Developers

```powershell
winget install OpenJS.NodeJS.LTS
winget install Microsoft.VisualStudioCode
```
