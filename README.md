# FK8s — Freddy's Kubernetes Provisioner

FK8s lets authorised GitHub users provision AKS Windows nodes on demand directly
from VS Code. A GitHub-authenticated Azure Function acts as the provisioning gate;
Terraform manages all Azure and GitHub infrastructure.

---

## Prerequisites

Requirements depend on what you are doing.

### End users — provisioning nodes via VS Code

Just [VS Code](https://code.visualstudio.com) with the FK8s extension installed.
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

### Ops / infrastructure — deploying or updating a customer cluster

The person who sets up and manages customer environments needs:

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

### Developers — working on the VS Code extension

| Tool | Install |
|---|---|
| [Node.js](https://nodejs.org) (LTS) | `winget install OpenJS.NodeJS.LTS` |
| [VS Code](https://code.visualstudio.com) | `winget install Microsoft.VisualStudioCode` |

---

## Credentials setup

Never commit secrets to source control. Set these as environment variables before deploying:

```powershell
$env:TF_VAR_github_token  = "<your-github-pat>"
$env:TF_VAR_sql_sa_password = "<your-sql-sa-password>"
```

Then log in to Azure:

```powershell
az login
az account set --subscription "<your-subscription-id>"
```

---

## Deploying a customer environment

1. Copy the example var file and fill in your values:
   ```powershell
   Copy-Item terraform/customers/example.tfvars terraform/customers/customer-a.tfvars
   # Edit customer-a.tfvars
   ```

2. Initialise Terraform (first time only):
   ```powershell
   cd terraform
   terraform init
   ```

3. Deploy:
   ```powershell
   .\deploy.ps1 -VarFile customers/customer-a.tfvars
   ```
   `deploy.ps1` runs `checkGitHubTeam.ps1` first (imports an existing GitHub team
   into Terraform state if needed) and then runs `terraform apply`.

---

## Updating the Azure Function only

To redeploy Function infrastructure and publish new function code without touching
the AKS cluster:

```powershell
cd terraform
.\deploy-function.ps1 -VarFile customers/customer-a.tfvars
```

---

## Using the FK8s CLI

Publish the executable from the repository root:

```powershell
cd fk8s-cli
dotnet publish -c Release -o .\dist
```

Run from the publish folder (or add it to PATH):

```powershell
cd .\dist
.k8s.exe createnode
```

Set the function base URL:

```powershell
# Edit fk8s.settings.json next to fk8s.exe
{
   "baseUrl": "https://<your-function-app>.azurewebsites.net/api"
}
```

Run commands:

```powershell
fk8s.exe createnode
fk8s.exe removenode
```

Pass optional payload parameters:

```powershell
fk8s.exe createnode --artifactUrl "https://example/artifact.zip" --adminUsername "admin" --adminPassword "P@ssword1"
fk8s.exe removenode --NodeUrl "https://node01.example.com"
```

---

## Repository structure

```
terraform/               Terraform configuration
  customers/             Per-customer .tfvars files (copy example.tfvars)
  deploy.ps1             Full environment deploy (GitHub check + terraform apply)
  deploy-function.ps1    Function-only deploy (targeted apply + func publish)
  checkGitHubTeam.ps1    Imports existing GitHub team into Terraform state
azure-function/          Azure Function source (C#, .NET 8, isolated worker)
fk8s-vsix/              VS Code extension source (TypeScript)
fk8s-cli/               C#/.NET command line interface
```
