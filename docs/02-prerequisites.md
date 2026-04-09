# Step 2: Install Prerequisites

## For Ops (deploying infrastructure)

Install all of these:

```powershell
winget install Hashicorp.Terraform
winget install Microsoft.AzureCLI
winget install Microsoft.PowerShell
winget install Microsoft.DotNet.SDK.8
winget install GitHub.cli
npm install -g azure-functions-core-tools@4
```

| Tool | Minimum Version | Purpose |
|------|----------------|---------|
| Terraform | 1.7.0 | Provisions all Azure + GitHub infrastructure |
| Azure CLI | 2.59.0 | Authentication to Azure for Terraform |
| PowerShell | 7.4 | Deployment scripts |
| .NET SDK | 8.0 | Building the Fkh backend |
| GitHub CLI | latest | GitHub token management |
| Azure Functions Core Tools | 4.x | Publishing the Function App |

Verify installations:

```powershell
terraform --version
az --version
pwsh --version
dotnet --version
gh --version
func --version
```

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
