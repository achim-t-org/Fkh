# Step 1 — Create the Private Deployment Repository

> **Performed by:** GitHub Organization Administrator

Before anything else, you need a private repository in your GitHub organization. This is where your configuration, secrets, and deployment workflows live. The workflows in this repository delegate to reusable workflows in the public Fkh fork.

## Why a separate deployment repository?

Fkh uses a **two-repo model**:

| Repository | Visibility | Contains |
|---|---|---|
| **Fkh fork** | Public | Reusable workflows, Terraform modules, backend code, CLI, VS Code extension |
| **Deployment repo** | **Private** | Your `deployment.tfvars`, caller workflows, GitHub Secrets |

The deployment repo is private because it holds your organization-specific configuration (Azure subscription IDs, GitHub org names, team members) and is where you store secrets (SQL passwords, GitHub App private keys, PATs). The caller workflows in this repo pass those secrets to the reusable workflows in your Fkh fork.

### One fork, many deployments

Every Fkh deployment needs its own dedicated deployment repository. However, you only need to **fork the Fkh repository once** per GitHub organization. A single Fkh fork can serve any number of deployment repositories.

For example, an organization managing multiple customer environments would have:

```
my-company/Fkh                             ← forked once (public)
my-company/fkh-deploy-contoso              ← deployment repo for Contoso
my-company/fkh-deploy-migrateFabrikam      ← deployment repo for Fabrikam migration
my-company/fkh-deploy-litware              ← deployment repo for Litware
```

Each deployment repo has its own configuration and can have its own secrets, Azure subscription, and deployment identity — completely independent of the others.

However, if multiple deployments share the same Azure subscription and deployment identity, you can avoid duplicating secrets by storing them as **organization-level secrets** in GitHub (Settings → Secrets and variables → Actions → Organization secrets). Organization secrets can be scoped to selected repositories, so you can share them across your `fkh-deploy-*` repos without exposing them to unrelated repositories. In that case, you only need to create the deployment identity once (Step 2) and add a federated credential for each deployment repo that uses it.

## 1.1 — Fork the Fkh repository

If you haven't already, fork the Fkh repository into your GitHub organization. This gives you the reusable workflows, Terraform modules, and all source code.

1. Go to the [Fkh repository](https://github.com/Freddy-DK/Fkh) on GitHub.
2. Click **Fork** → under **Owner**, select your GitHub organization.
3. Keep the repository name as `Fkh`.
4. Click **Create fork**.

> This fork is public by default. That's fine — it contains no secrets or organization-specific configuration.

## 1.2 — Create the private deployment repository

1. Go to your GitHub organization → **Repositories** → **New repository**.
2. Set the repository name using the pattern `fkh-deploy-<deploymentname>`, where `<deploymentname>` identifies this specific deployment (e.g. `fkh-deploy-contoso`, `fkh-deploy-migrateFabrikam`, `fkh-deploy-litware`).
3. Set visibility to **Private**.
4. Check **Add a README file** (optional).
5. Click **Create repository**.

> **Naming convention:** Using `fkh-deploy-<deploymentname>` makes it easy to identify Fkh deployment repos at a glance and keeps them grouped alphabetically. The `<deploymentname>` part is up to you — use your environment name, region, team, or whatever makes sense.

## 1.3 — Copy the deployment template into your repository

The Fkh repository includes a `deployment-repo/` folder with a starter configuration and workflow templates.

1. In your Fkh fork, navigate to the `deployment-repo/` folder.
2. Copy the contents into the root of your new private deployment repository. You should end up with:

```
your-deployment-repo/
├── config/
│   └── deployment.tfvars    # Your environment configuration (edit in a later step)
├── .github/
│   └── workflows/           # Caller workflows (copied from deployment-repo)
└── README.md
```

> **Tip:** You can do this via the GitHub web UI (download as ZIP and upload) or clone both repos locally and copy the files.

## 1.4 — Note your repository details

Record these values — you will need them in the next step when setting up the federated credential for the deployment identity:

| Value | Example |
|-------|--------|
| **GitHub organization name** | `my-company` |
| **Deployment repository name** | `fkh-deploy-contoso` |
| **Fkh fork repository name** | `Fkh` |

> **Important:** The federated credential you create in Step 2 must reference the **deployment repository** name (e.g. `fkh-deploy-contoso`), not `Fkh`. The deployment workflows run from this private repo, so the OIDC token issued by GitHub Actions will have this repo's name in the subject claim.

---

*Previous: [Roles and Overview](README.md)*
*Next: [Step 2 — Create the Azure Deployment Identity](Step2-AzureIdentity.md)*
