# Fkh Deployment Repository

This is a private deployment repository for your [Fkh](https://github.com/Freddy-DK/Fkh) infrastructure. It contains your Terraform configuration and thin caller workflows that delegate to reusable workflows in your Fkh fork.

## Two-Repo Model

| Repository | Visibility | Purpose |
|---|---|---|
| **Your Fkh fork** | Public | Contains the reusable workflows, Terraform modules, backend code, and CLI |
| **This repo** (deployment) | Private | Contains your `deployment.tfvars` and caller workflows that pass secrets |

## Setup

### 1. Configure `config/deployment.tfvars`

Edit `config/deployment.tfvars` with your Azure subscription, GitHub org, and other settings. See the comments in the file for guidance.

**Never commit secrets** to this file. Use GitHub Secrets instead (see below).

### 2. Set up GitHub Secrets

Go to **Settings > Secrets and variables > Actions** and add:

| Secret | Description |
|---|---|
| `AZURE_DEPLOY_CLIENT_ID` | Client ID of your deployment identity (App Registration or Managed Identity) |
| `SQL_SA_PASSWORD` | SA password for the SQL Server deployed in AKS |
| `GH_APP_PRIVATE_KEY` | PEM-encoded private key of the GitHub App |

> **Note:** `tenant_id` and `subscription_id` are read from `config/deployment.tfvars` automatically — no secrets needed for those.

### 3. Deploy

Run the **Deploy Full Stack** workflow from the Actions tab to create all Azure infrastructure and publish the backend.

After the initial deployment, use **Update Backend** to publish code changes without re-running Terraform.

### 4. Create Images

The **Create Images** workflow can run either:
- **In your public Fkh fork** (free, but publicly visible which images you build)
- **In this private repo** (paid runners, but private)

Set `create_images_repo` in your `deployment.tfvars` to the org/repo where the backend dispatches image builds to.

## Keeping Up to Date

The **Check for Deployment Repo Updates** workflow runs weekly and creates a GitHub Issue if your workflow files differ from the templates in your Fkh fork. You can also run it manually from the Actions tab.
