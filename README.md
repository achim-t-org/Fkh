# Fkh — Freddy's Kubernetes Helper

Fkh lets authorised GitHub users provision AKS Windows nodes on demand directly
from VS Code. A GitHub-authenticated Azure Function acts as the provisioning gate;
Terraform manages all Azure and GitHub infrastructure.

---

## Prerequisites

Requirements depend on what you are doing.

### End users — provisioning nodes via VS Code

Just [VS Code](https://code.visualstudio.com) with the Fkh extension installed.
Authentication is handled automatically via VS Code's built-in GitHub sign-in.
No Azure CLI, no Terraform, nothing else.

### End users — provisioning nodes via CLI

Install these tools:

| Tool | Minimum version | Install |
|---|---|---|
| [.NET SDK](https://dot.net) | 8.0 | `winget install Microsoft.DotNet.SDK.8` |
| [GitHub CLI](https://cli.github.com/) | latest | `winget install GitHub.cli` |

Then sign in once:

```powershell
gh auth login
```

The CLI uses `gh auth token` automatically for authentication.

### Ops / infrastructure — deploying or updating an organization cluster

The person who sets up and manages organization environments needs:

| Tool | Minimum version | Install |
|---|---|---|
| [Terraform](https://developer.hashicorp.com/terraform/install) | 1.7.0 | `winget install Hashicorp.Terraform` |
| [Azure CLI](https://aka.ms/installazurecli) | 2.59.0 | `winget install Microsoft.AzureCLI` |
| [PowerShell](https://aka.ms/powershell) | 7.4 | `winget install Microsoft.PowerShell` |
| [.NET SDK](https://dot.net) | 8.0 | `winget install Microsoft.DotNet.SDK.8` |
| [Azure Functions Core Tools](https://aka.ms/azure-functions-core-tools) | 4.x | `npm install -g azure-functions-core-tools@4` |

And the following accounts / permissions:

| What | Why |
|---|---|
| Azure subscription | Target for all infrastructure (AKS, Function App, storage, identity) |
| Azure account with **Contributor** role on the subscription | Terraform creates resource groups, AKS clusters, Function Apps |
| GitHub **Personal Access Token** with `admin:org` scope | Terraform creates and manages the GitHub team |
| **GitHub App** per organization ([setup guide](docs/github-app-setup.md)) | Function App triggers image-build workflows automatically |

### Developers — working on the VS Code extension

| Tool | Install |
|---|---|
| [Node.js](https://nodejs.org) (LTS) | `winget install OpenJS.NodeJS.LTS` |
| [VS Code](https://code.visualstudio.com) | `winget install Microsoft.VisualStudioCode` |

---

## Credentials setup

Never commit secrets to source control. Set these as environment variables before deploying:

```powershell
$env:TF_VAR_github_token          = "<your-github-pat>"
$env:TF_VAR_sql_sa_password        = "<your-sql-sa-password>"
$env:TF_VAR_github_app_private_key = Get-Content "<path-to>.pem" -Raw
```

Then log in to Azure:

```powershell
az login
az account set --subscription "<your-subscription-id>"
```

---

## Deploying an organization environment

1. Copy the example var file and fill in your values:
   ```powershell
   Copy-Item terraform/organizations/example.tfvars terraform/organizations/my-org.tfvars
   # Edit my-org.tfvars
   ```

2. Initialise Terraform (first time only):
   ```powershell
   cd terraform
   terraform init
   ```

3. Deploy:
   ```powershell
   .\deploy.ps1 -VarFile organizations/my-org.tfvars
   ```
   `deploy.ps1` runs `checkGitHubTeam.ps1` first (imports an existing GitHub team
   into Terraform state if needed), runs `terraform apply`, and then publishes
   the Azure Function code.

---

## Updating the Azure Function only

To publish updated function code without touching Terraform-managed infrastructure:

```powershell
cd terraform
.\deploy-functionupdate.ps1
```

---

## Using the Fkh CLI

Publish the executable from the repository root:

```powershell
cd fkh-cli
dotnet publish -c Release -o .\dist
```

Run from the publish folder (or add it to PATH):

```powershell
cd .\dist
.\fkh.exe createnode
```

Set the function base URL:

```powershell
# Edit fkh.settings.json next to fkh.exe
{
   "baseUrl": "https://<your-function-app>.azurewebsites.net/api"
}
```

Run commands:

```powershell
fkh.exe listnodes
fkh.exe createnode
fkh.exe removenode
fkh.exe stopnode
fkh.exe startnode
fkh.exe allowsqlaccess
fkh.exe revokesqlaccess
```

Pass optional payload parameters:

```powershell
fkh.exe listnodes --all
fkh.exe createnode --name bcserver --artifactUrl "https://example/artifact.zip" --adminUsername "admin" --adminPassword "P@ssword1"
fkh.exe removenode --name bcserver
fkh.exe stopnode --name bcserver
fkh.exe startnode --name bcserver
fkh.exe allowsqlaccess                          # auto-detects your public IP
fkh.exe allowsqlaccess --ip 203.0.113.10 --hours 4
fkh.exe revokesqlaccess
```

### Direct SQL Server access

Use `allowsqlaccess` to temporarily expose the shared SQL Server to your public IP.
The command returns a SQL endpoint (`<ip>,1433`) you can connect to from SSMS or
Azure Data Studio using the SA credentials. Access auto-revokes after the specified
number of hours (default 2). Revoke manually anytime with `revokesqlaccess`.

---

## VS Code extension settings

The extension reads parameter defaults from VS Code settings using the pattern
`fkh.<FunctionName>.<parameterName>`. If a value is set, that parameter is
skipped during prompting. These settings are fully dynamic — any parameter from
the function catalog can be defaulted without rebuilding the extension.

Examples (add to `settings.json`):

```json
{
  "fkh.baseUrl": "https://fkhmyapp.azurewebsites.net/api",
  "fkh.CreateNode.adminUsername": "admin",
  "fkh.StartNode.autostop": "4",
  "fkh.AllowSqlAccess.hours": "4"
}
```

The `ip` parameter for `AllowSqlAccess` is auto-detected via `api.ipify.org`
in both the VS Code extension and the CLI.

---

## Repository structure

```
terraform/               Terraform configuration
  organizations/             Per-organization .tfvars files (copy example.tfvars)
   deploy.ps1             Full environment deploy (GitHub check + terraform apply + function publish)
   deploy-functionupdate.ps1 Function code publish only
  checkGitHubTeam.ps1    Imports existing GitHub team into Terraform state
fkh-backend/            FKH backend source (C#, .NET 8, isolated worker)
fkh-vsix/              VS Code extension source (TypeScript)
fkh-cli/               C#/.NET command line interface (builds as fkh.exe)
```
